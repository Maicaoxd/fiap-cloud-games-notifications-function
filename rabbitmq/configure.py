"""Provisionamento local idempotente, sem usuarios, senhas ou exclusoes nas definitions."""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

base = os.environ.get("RABBITMQ_ADMIN_URL", "http://rabbitmq:15672").rstrip("/")
credentials = f"{os.environ['RABBITMQ_USERNAME']}:{os.environ['RABBITMQ_PASSWORD']}"
headers = {"Authorization": "Basic " + base64.b64encode(credentials.encode()).decode(),
           "Content-Type": "application/json"}

def call(method, path, body=None, allow_missing=False):
    request = urllib.request.Request(base + path,
        data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            data = response.read()
            return json.loads(data) if data else None
    except urllib.error.HTTPError as error:
        if allow_missing and error.code == 404:
            return None
        raise RuntimeError(f"RabbitMQ retornou HTTP {error.code} em {method} {path}") from None

try:
    for attempt in range(60):
        try:
            call("GET", "/api/overview")
            break
        except (urllib.error.URLError, TimeoutError):
            if attempt == 59:
                raise RuntimeError("RabbitMQ Management indisponivel") from None
            time.sleep(2)

    with open("/config/definitions.json", encoding="utf-8") as source:
        definitions = json.load(source)
    # Falhar antes de importar se entidades existentes forem incompativeis; nunca purgar/recriar.
    for kind in ("exchanges", "queues"):
        for entity in definitions[kind]:
            path = f"/api/{kind}/%2F/{urllib.parse.quote(entity['name'], safe='')}"
            existing = call("GET", path, allow_missing=True)
            fields = ("durable", "auto_delete", "arguments") + (("type", "internal") if kind == "exchanges" else ())
            if existing and any(existing.get(field) != entity[field] for field in fields):
                raise RuntimeError(f"Entidade RabbitMQ incompativel: {entity['name']}")
    call("POST", "/api/definitions", definitions)
    for kind in ("exchanges", "queues"):
        for entity in definitions[kind]:
            call("GET", f"/api/{kind}/%2F/{urllib.parse.quote(entity['name'], safe='')}")
    bindings = call("GET", "/api/bindings/%2F")
    for expected in definitions["bindings"]:
        if not any(all(binding.get(field) == expected[field] for field in
            ("source", "destination", "destination_type", "routing_key", "arguments")) for binding in bindings):
            raise RuntimeError("Binding de notificacao nao confirmado apos importacao")
    print("Filas e exchanges de notificacao configurados sem excluir dados.")
except (RuntimeError, urllib.error.URLError, TimeoutError) as error:
    # Nao imprimir payloads, credenciais ou objetos de conexao.
    print(str(error) if isinstance(error, RuntimeError) else "Falha de conexao RabbitMQ", file=sys.stderr)
    sys.exit(1)
