<#
.SYNOPSIS
    Authors the EventTracer test fixture into an FMOD Studio project and builds its bank.

.DESCRIPTION
    The PlayMode tests for module A assert one PlaybackOutcome each, and every
    outcome needs an event configured to provoke it - max instances of one,
    a stealing mode, a 3D range narrow enough to fall out of. Rather than ask
    anyone to reproduce that by hand, this drives FMOD Studio's command line
    tool over a script that authors the events, then builds the bank.

    Safe to re-run: existing fixture events are reconfigured, not duplicated.

.EXAMPLE
    ./build-trace-fixture.ps1 -Project E:\AudioProgramming\Audio\Audio.fspro
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Project,

    # Defaults to the newest FMOD Studio found under Program Files.
    [string] $StudioPath
)

$ErrorActionPreference = 'Stop'

function Resolve-StudioCli {
    param([string] $Explicit)

    if ($Explicit) {
        $candidate = if ($Explicit.EndsWith('.exe')) { $Explicit } else { Join-Path $Explicit 'fmodstudiocl.exe' }
        if (-not (Test-Path $candidate)) { throw "No fmodstudiocl.exe at $candidate" }
        return $candidate
    }

    $roots = @(
        'C:\Program Files\FMOD SoundSystem',
        'C:\Program Files (x86)\FMOD SoundSystem'
    ) | Where-Object { Test-Path $_ }

    $found = $roots |
        ForEach-Object { Get-ChildItem $_ -Directory -Filter 'FMOD Studio *' } |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'fmodstudiocl.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $found) { throw 'FMOD Studio not found. Pass -StudioPath.' }
    return $found
}

# A 16-bit mono PCM sine. Written by hand because the fixture must not depend on
# an audio asset someone has to supply, and -20 dBFS because these events get
# posted a few hundred times by the performance tests.
function New-SineWav {
    param(
        [string] $Path,
        [double] $Seconds,
        [double] $Frequency,
        [int]    $SampleRate = 48000,
        [double] $Amplitude = 0.1
    )

    $sampleCount = [int]($SampleRate * $Seconds)
    $dataBytes = $sampleCount * 2

    $stream = [System.IO.File]::Create($Path)
    try {
        $writer = New-Object System.IO.BinaryWriter($stream)

        $writer.Write([char[]]'RIFF')
        $writer.Write([uint32](36 + $dataBytes))
        $writer.Write([char[]]'WAVE')
        $writer.Write([char[]]'fmt ')
        $writer.Write([uint32]16)          # PCM chunk size
        $writer.Write([uint16]1)           # format: PCM
        $writer.Write([uint16]1)           # channels
        $writer.Write([uint32]$SampleRate)
        $writer.Write([uint32]($SampleRate * 2))  # byte rate
        $writer.Write([uint16]2)           # block align
        $writer.Write([uint16]16)          # bits per sample
        $writer.Write([char[]]'data')
        $writer.Write([uint32]$dataBytes)

        # A short fade at each end keeps the loop-free asset from clicking.
        $fade = [Math]::Min(2400, [int]($sampleCount / 4))
        $step = 2.0 * [Math]::PI * $Frequency / $SampleRate

        for ($i = 0; $i -lt $sampleCount; $i++) {
            $envelope = 1.0
            if ($i -lt $fade) { $envelope = $i / $fade }
            elseif ($i -gt ($sampleCount - $fade)) { $envelope = ($sampleCount - $i) / $fade }

            $value = [Math]::Sin($step * $i) * $Amplitude * $envelope
            $writer.Write([int16]([Math]::Round($value * 32767)))
        }

        $writer.Flush()
    }
    finally {
        $stream.Dispose()
    }

    Write-Host "wrote $Path ($Seconds s, $Frequency Hz)"
}

$projectPath = (Resolve-Path $Project).Path
$studioCli = Resolve-StudioCli -Explicit $StudioPath
$scriptDir = $PSScriptRoot

Write-Host "FMOD Studio CLI : $studioCli"
Write-Host "FMOD project    : $projectPath"

# Staged next to the project rather than in TEMP so a failed import leaves
# something inspectable, and so importAudioFile copies from a stable location.
$stageDir = Join-Path (Split-Path $projectPath -Parent) '.tracefixture'
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$longWav = Join-Path $stageDir 'trace_tone_4s.wav'
New-SineWav -Path $longWav -Seconds 4.0 -Frequency 220

# The authoring script is version-controlled and path-free; the paths arrive as
# a generated prelude so nothing has to be templated inside the JavaScript.
$prelude = @"
var FIXTURE = {
    projectPath: $($projectPath -replace '\\', '/' | ConvertTo-Json),
    longWavPath: $($longWav -replace '\\', '/' | ConvertTo-Json)
};
"@

$generated = Join-Path $stageDir 'build-trace-fixture.generated.js'
$body = Get-Content (Join-Path $scriptDir 'build-trace-fixture.js') -Raw
Set-Content -Path $generated -Value ($prelude + "`n" + $body) -Encoding UTF8

& $studioCli -script $generated $projectPath
if ($LASTEXITCODE -ne 0) {
    throw "fmodstudiocl exited with $LASTEXITCODE"
}

Write-Host 'Fixture built.'
