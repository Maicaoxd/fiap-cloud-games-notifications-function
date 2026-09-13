#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$AdminUrl = $(if ($env:RABBITMQ_ADMIN_URL) { $env:RABBITMQ_ADMIN_URL } else { 'http://rabbitmq:15672' }),
    [string]$DefinitionsPath = (Join-Path $PSScriptRoot 'definitions.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Stop-RabbitMQConfiguration {
    param([string]$Message, [switch]$ConnectionFailure)
    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['MensagemSegura'] = $true
    $exception.Data['FalhaDeConexao'] = [bool]$ConnectionFailure
    throw $exception
}

function Invoke-RabbitMQRequest {
    param([string]$Method, [string]$Path, [object]$Body, [switch]$AllowMissing)
    $parameters = @{
        Uri = "$script:BaseUrl$Path"
        Method = $Method
        Headers = $script:RequestHeaders
        TimeoutSec = 5
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 100 -Compress))
    }
    try {
        $response = Invoke-WebRequest @parameters
    }
    catch {
        # Não propaga objetos de conexão, credenciais ou respostas do servidor.
        Stop-RabbitMQConfiguration -Message 'Falha de conexão com RabbitMQ Management.' -ConnectionFailure
    }
    $status = [int]$response.StatusCode
    if ($AllowMissing -and $status -eq 404) { return $null }
    if ($status -lt 200 -or $status -ge 300) {
        Stop-RabbitMQConfiguration -Message "RabbitMQ retornou HTTP $status em $Method $Path."
    }
    if ([string]::IsNullOrWhiteSpace($response.Content)) { return $null }
    try { return ConvertFrom-Json -InputObject $response.Content -AsHashtable -Depth 100 }
    catch { Stop-RabbitMQConfiguration -Message 'RabbitMQ retornou uma resposta JSON inválida.' }
}

function Test-JsonValueEqual {
    param([object]$Actual, [object]$Expected)
    if ($null -eq $Actual -or $null -eq $Expected) { return $null -eq $Actual -and $null -eq $Expected }
    if ($Expected -is [Collections.IDictionary]) {
        if ($Actual -isnot [Collections.IDictionary] -or $Actual.Count -ne $Expected.Count) { return $false }
        foreach ($key in $Expected.Keys) {
            if ($Actual.Keys -cnotcontains $key) { return $false }
            if (-not (Test-JsonValueEqual $Actual[$key] $Expected[$key])) { return $false }
        }
        return $true
    }
    if ($Expected -is [array]) {
        if ($Actual -isnot [array] -or $Actual.Count -ne $Expected.Count) { return $false }
        for ($index = 0; $index -lt $Expected.Count; $index++) {
            if (-not (Test-JsonValueEqual $Actual[$index] $Expected[$index])) { return $false }
        }
        return $true
    }
    return $Actual.GetType() -eq $Expected.GetType() -and $Actual -ceq $Expected
}

function Assert-RabbitMQEntity {
    param([Collections.IDictionary]$Actual, [Collections.IDictionary]$Expected, [string]$Kind)
    $fields = @('durable', 'auto_delete', 'arguments')
    if ($Kind -eq 'exchanges') { $fields += @('type', 'internal') }
    foreach ($field in $fields) {
        if (-not $Actual.Contains($field) -or -not (Test-JsonValueEqual $Actual[$field] $Expected[$field])) {
            Stop-RabbitMQConfiguration -Message "Entidade RabbitMQ incompatível: $($Expected.name), campo $field."
        }
    }
}

function Get-RabbitMQEntityPath {
    param([string]$Kind, [Collections.IDictionary]$Entity)
    $virtualHost = [Uri]::EscapeDataString($Entity.vhost)
    $entityName = [Uri]::EscapeDataString($Entity.name)
    return "/api/$Kind/$virtualHost/$entityName"
}

try {
    $adminUri = $null
    if (-not [Uri]::TryCreate($AdminUrl, [UriKind]::Absolute, [ref]$adminUri) -or
        $adminUri.Scheme -notin @('http', 'https') -or $adminUri.UserInfo -or
        $adminUri.Query -or $adminUri.Fragment -or $adminUri.AbsolutePath -ne '/') {
        Stop-RabbitMQConfiguration -Message 'RABBITMQ_ADMIN_URL deve ser uma URL HTTP/HTTPS sem credenciais, caminhos ou parâmetros.'
    }
    if ([string]::IsNullOrWhiteSpace($env:RABBITMQ_USERNAME) -or [string]::IsNullOrWhiteSpace($env:RABBITMQ_PASSWORD)) {
        Stop-RabbitMQConfiguration -Message 'Configure RABBITMQ_USERNAME e RABBITMQ_PASSWORD.'
    }
    $script:BaseUrl = $AdminUrl.TrimEnd('/')
    $credentials = "$($env:RABBITMQ_USERNAME):$($env:RABBITMQ_PASSWORD)"
    $script:RequestHeaders = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($credentials)) }

    try { $definitions = Get-Content -LiteralPath $DefinitionsPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable -Depth 100 }
    catch { Stop-RabbitMQConfiguration -Message 'Não foi possível ler definitions.json como JSON válido.' }
    if ($definitions -isnot [Collections.IDictionary] -or $definitions.Count -ne 3 -or
        @('exchanges', 'queues', 'bindings').Where({ -not $definitions.Contains($_) }).Count -gt 0) {
        Stop-RabbitMQConfiguration -Message 'As definições devem conter somente exchanges, queues e bindings.'
    }
    foreach ($kind in @('exchanges', 'queues', 'bindings')) {
        if ($definitions[$kind] -isnot [array] -or $definitions[$kind].Count -eq 0) {
            Stop-RabbitMQConfiguration -Message "A lista $kind deve conter as entidades de notificação."
        }
        foreach ($entity in $definitions[$kind]) {
            if ($entity -isnot [Collections.IDictionary] -or $entity['vhost'] -cne '/') {
                Stop-RabbitMQConfiguration -Message 'As definições devem usar somente o virtual host /.'
            }
        }
    }

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try { $null = Invoke-RabbitMQRequest -Method GET -Path '/api/overview'; break }
        catch {
            if (-not $_.Exception.Data['FalhaDeConexao']) { throw }
            if ($attempt -eq 60) { Stop-RabbitMQConfiguration -Message 'RabbitMQ Management indisponível após aguardar a inicialização.' }
            Start-Sleep -Seconds 2
        }
    }

    # Verifica compatibilidade antes de importar; nunca exclui ou recria entidades.
    foreach ($kind in @('exchanges', 'queues')) {
        foreach ($entity in $definitions[$kind]) {
            $existing = Invoke-RabbitMQRequest -Method GET -Path (Get-RabbitMQEntityPath $kind $entity) -AllowMissing
            if ($null -ne $existing) { Assert-RabbitMQEntity $existing $entity $kind }
        }
    }
    $null = Invoke-RabbitMQRequest -Method POST -Path '/api/definitions' -Body $definitions
    foreach ($kind in @('exchanges', 'queues')) {
        foreach ($entity in $definitions[$kind]) {
            $actual = Invoke-RabbitMQRequest -Method GET -Path (Get-RabbitMQEntityPath $kind $entity)
            Assert-RabbitMQEntity $actual $entity $kind
        }
    }
    $bindings = @(Invoke-RabbitMQRequest -Method GET -Path '/api/bindings/%2F')
    foreach ($expected in $definitions.bindings) {
        $confirmed = $false
        foreach ($binding in $bindings) {
            $matches = $true
            foreach ($field in @('source', 'destination', 'destination_type', 'routing_key', 'arguments')) {
                if (-not $binding.Contains($field) -or -not (Test-JsonValueEqual $binding[$field] $expected[$field])) { $matches = $false; break }
            }
            if ($matches) { $confirmed = $true; break }
        }
        if (-not $confirmed) { Stop-RabbitMQConfiguration -Message 'Ligação de notificação não confirmada após a importação.' }
    }
    Write-Host 'Filas, exchanges e ligações de notificação configuradas sem excluir dados.'
    exit 0
}
catch {
    $message = if ($_.Exception.Data['MensagemSegura']) { $_.Exception.Message } else { 'Falha inesperada ao configurar o RabbitMQ. Confira as definições e o acesso administrativo.' }
    [Console]::Error.WriteLine($message)
    exit 1
}
