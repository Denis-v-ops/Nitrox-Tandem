<#
.SYNOPSIS
    COOP_FEATURES_SPEC R2c: Bob's game shows the beam, sparks and sound of the tool in Alice's hand while she uses it:
    scanner, builder, welder and laser cutter, and stops them when she stops.

.DESCRIPTION
    Alice draws each tool. Her tool use is driven as in R2b-PartnerArms.ps1: windowless, the game never sets using_tool on
    the sender (GUIHand.GetUsingTool needs AvatarInputHandler.IsEnabled(), false without a locked cursor), so the scenario
    turns Alice's ArmsController off and writes using_tool / using_tool_alt on her own arms animator. What is tested is
    her game's watcher, the relay, and Bob's copy of her held tool (the same item id in Bob's game):
    - scanner: ScannerTool.scanBeam active, its scan loop playing, the scanner's own animator in using_tool; the same for
      the self-scan (using_tool_alt); all off after;
    - builder: Alice places a corridor ghost and builds it frame by frame (BuilderTool.Construct, what holding the button
      calls) while using_tool is on. Bob's copy beams at the ghost his game sees being built (BuilderTool.LateUpdate draws
      the beams from it): beams active, the build loop playing, the target the ghost. With using_tool on and nothing
      being built, no beam;
    - welder: the sparks (VFXController emitter instance, underwater index 1) and the weld loop (FMODASRPlayer), twice;
    - laser cutter: its beam effect and light, twice. Its sound isn't played by Bob's copy: it's already relayed.
    Before R2c, none of this showed on Bob's copy (the tools' effects run only for the player who holds them).

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\R2c-PartnerToolEffects.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'r2c-tool-effects', [string]$BuildSpot = '-40 -42 360', [string]$GhostTechType = 'BaseCorridorI')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'

function Get-Seen { param([string]$Parameter) (Invoke-BridgeJson $B "animator $script:aliceId")[0].parameters.$Parameter }

function Get-OnBob { param([string]$Path) Invoke-Bridge $B "get $Path" -AllowError }

function Assert-OnBob {
    param([string]$Path, [string]$Expected, [string]$Message, [int]$TimeoutSeconds = 8)
    $matched = Wait-Until { (Get-OnBob $Path) -eq $Expected } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message ($Path = $(Get-OnBob $Path))"
}

# A Unity object reference reads 'null' once it's gone
function Assert-ObjectOnBob {
    param([string]$Path, [bool]$Exists, [string]$Message, [int]$TimeoutSeconds = 8)
    $matched = Wait-Until { ((Get-OnBob $Path) -notin @('null', '') -and -not (Get-OnBob $Path).StartsWith('ERR')) -eq $Exists } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message ($Path = $(Get-OnBob $Path))"
}

function Assert-Playing {
    param([string]$Expected, [string]$Message)
    Assert-OnBob "$script:aliceId RemoteToolEffects.Playing" $Expected $Message
}

function Select-Tool {
    param([string]$TechType)
    $id = (Get-InventoryItem $A $TechType | Select-Object -First 1).id
    $slot = [int](Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $id")
    Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $slot" | Out-Null
    $script:toolId = $id
    $id
}

# Alice's arms animator, written by the scenario while her ArmsController is off (see the description)
function Set-OwnArmState {
    param([string]$Parameter, [bool]$Value)
    Invoke-Bridge $A "call Player.main.armsController.animator.SetBool $Parameter $($Value.ToString().ToLowerInvariant())" | Out-Null
}

function Use-Tool {
    param([string]$Parameter = 'using_tool', [bool]$Value)
    Set-OwnArmState $Parameter $Value
    $matched = Wait-Until { (Get-Seen $Parameter) -eq $Value } -TimeoutSeconds 8 -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "Bob's copy of Alice has $Parameter = $Value"
}

function Wait-HoldsOnBob {
    param([string]$AnimToolName)
    $matched = Wait-Until { (Get-Seen "holding_$AnimToolName") -eq $true } -TimeoutSeconds 10 -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "Bob sees Alice hold the $AnimToolName"
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice is at the build spot with a scanner, a builder, a welder and a laser cutter; Bob 6 m away' {
        $spot = $BuildSpot -split ' ' | ForEach-Object { [double]$_ }
        Invoke-Bridge $A "teleport $BuildSpot" | Out-Null
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f ($spot[0] + 6), $spot[1], $spot[2]) | Out-Null
        foreach ($tool in 'scanner', 'builder', 'welder', 'lasercutter') { Invoke-Bridge $A "console item $tool" | Out-Null }
        $all = Wait-Until { (Get-InventoryItem $A 'Scanner') -and (Get-InventoryItem $A 'Builder') -and (Get-InventoryItem $A 'Welder') -and (Get-InventoryItem $A 'LaserCutter') } -TimeoutSeconds 30
        Assert-True ([bool]$all) 'Alice has the four tools'
        Invoke-Bridge $A 'call GameModeUtils.DeactivateCheat NoCost' | Out-Null
        if ((Invoke-Bridge $A 'get NoCostConsoleCommand.main.fastBuildCheat') -eq 'true') {
            Invoke-Bridge $A 'console fastbuild' | Out-Null
        }
        Start-Sleep -Seconds 2
        Assert-Playing 'NONE' "Bob's copy of Alice plays no tool effect"
    }

    Step 'Alice scans, then scans herself' {
        $scanner = Select-Tool 'Scanner'
        Wait-HoldsOnBob 'scanner'
        Assert-OnBob "$scanner ScannerTool.scanBeam.activeSelf" 'false' "Bob's copy of the scanner shows no beam yet"
        Invoke-Bridge $A 'set Player.main.armsController.enabled false' | Out-Null
        try {
            Use-Tool -Value $true
            Assert-Playing 'SCANNER' "Bob's game plays Alice's scanner"
            Assert-OnBob "$scanner ScannerTool.scanBeam.activeSelf" 'true' "Bob sees the scan beam"
            Assert-OnBob "$scanner ScannerTool.scanSound.playing" 'true' "Bob's game plays the scan loop on her scanner"
            $nozzle = Wait-Until { (Invoke-BridgeJson $B "animator $scanner")[0].parameters.using_tool -eq $true } -TimeoutSeconds 5 -IntervalSeconds 0.25
            Assert-True ([bool]$nozzle) "the scanner's own animator on Bob's copy is in using_tool"
            Use-Tool -Value $false
            Assert-OnBob "$scanner ScannerTool.scanBeam.activeSelf" 'false' "the scan beam goes off"
            Assert-OnBob "$scanner ScannerTool.scanSound.playing" 'false' "the scan loop stops"
            Assert-Playing 'NONE' "Bob's game stops Alice's scanner"

            Use-Tool 'using_tool_alt' $true
            Assert-OnBob "$scanner ScannerTool.scanBeam.activeSelf" 'true' "Bob sees the beam of her self-scan"
            Use-Tool 'using_tool_alt' $false
            Assert-OnBob "$scanner ScannerTool.scanBeam.activeSelf" 'false' "the self-scan beam goes off"
        }
        finally {
            Invoke-Bridge $A 'set Player.main.armsController.enabled true' | Out-Null
        }
    }

    Step "Alice places a $GhostTechType ghost" {
        $before = @(Invoke-BridgeJson $A 'find ConstructableBase' | ForEach-Object { $_.id })
        Invoke-Bridge $A "call Builder.BeginAsync $GhostTechType" | Out-Null
        $placeable = $false
        foreach ($pitch in -20, -10, -30, -15, -25, -5, -40) {
            Invoke-Bridge $A "look 0 $pitch" | Out-Null
            $placeable = Wait-Until { (Invoke-Bridge $A 'get Builder.canPlace') -eq 'true' } -TimeoutSeconds 3 -IntervalSeconds 0.5
            if ($placeable) { break }
        }
        if (-not $placeable) {
            Invoke-Bridge $A 'call Builder.End' | Out-Null
            throw "$A can't place $GhostTechType here (Builder.canPlace stays false)"
        }
        Assert-Equal 'true' (Invoke-Bridge $A 'call Builder.TryPlace') "$A placed the ghost"
        $ghost = Wait-Until { @(Invoke-BridgeJson $A 'find ConstructableBase') | Where-Object { $_.id -match '^[0-9a-f-]{36}$' -and $before -notcontains $_.id } | Select-Object -First 1 } -TimeoutSeconds 10
        if (-not $ghost) { throw 'no new ghost with an id' }
        $script:ghostId = $ghost.id
        Assert-True ([bool](Wait-Until { -not (Invoke-Bridge $B "entity $ghostId" -AllowError).StartsWith('ERR ') } -TimeoutSeconds 15)) "Bob's game has the ghost"
        foreach ($type in @(Invoke-BridgeJson $A "get $ghostId Constructable.resourceMap")) {
            Invoke-Bridge $A "console item $type 1" | Out-Null
        }
        Start-Sleep -Seconds 2
    }

    Step 'Alice builds the ghost; Bob sees the builder beams at it' {
        $builder = Select-Tool 'Builder'
        Wait-HoldsOnBob 'builder'
        Invoke-Bridge $A 'set Player.main.armsController.enabled false' | Out-Null
        try {
            Use-Tool -Value $true
            Start-Sleep -Seconds 1.5
            Assert-Playing 'BUILDER' "Bob's game knows Alice is building"
            Assert-OnBob "$builder BuilderTool.beamLeft.gameObject.activeSelf" 'false' "with nothing being built, Bob sees no beam" -TimeoutSeconds 1

            $seen = @{ beam = $false; sound = $false; target = $false; nozzle = $false }
            $end = (Get-Date).AddSeconds(6)
            $rounds = 0
            while ((Get-Date) -lt $end) {
                Invoke-Bridge $A "call $builder BuilderTool.Construct $ghostId true false" -AllowError | Out-Null
                $rounds++
                if ((Get-OnBob "$builder BuilderTool.beamLeft.gameObject.activeSelf") -eq 'true' -and (Get-OnBob "$builder BuilderTool.beamRight.gameObject.activeSelf") -eq 'true') { $seen.beam = $true }
                if ((Get-OnBob "$builder BuilderTool.buildSound.playing") -eq 'true') { $seen.sound = $true }
                if ((Get-OnBob "$script:aliceId RemoteToolEffects.BuildTarget") -match $ghostId) { $seen.target = $true }
                if ((Invoke-BridgeJson $B "animator $builder")[0].parameters.using_tool -eq $true) { $seen.nozzle = $true }
            }
            $amount = Invoke-Bridge $B "get $ghostId Constructable.constructedAmount"
            Write-Host "  $rounds build calls; the ghost is at $amount in Bob's game"
            Assert-True ([double]$amount -gt 0 -and [double]$amount -lt 1) "the ghost is partly built in Bob's game ($amount)"
            Assert-True $seen.beam "Bob sees both builder beams while Alice builds"
            Assert-True $seen.sound "Bob's game plays the build loop on her builder"
            Assert-True $seen.target "Bob's copy beams at the ghost being built"
            Assert-True $seen.nozzle "the builder's own animator on Bob's copy is in using_tool"
            Use-Tool -Value $false
            Assert-Playing 'NONE' "Bob's game stops Alice's builder"
            Assert-OnBob "$builder BuilderTool.beamLeft.gameObject.activeSelf" 'false' 'the builder beams go off'
            Assert-OnBob "$builder BuilderTool.buildSound.playing" 'false' 'the build loop stops'
        }
        finally {
            Invoke-Bridge $A 'set Player.main.armsController.enabled true' | Out-Null
        }
    }

    foreach ($tool in @(
            @{ TechType = 'Welder'; Anim = 'welder'; Kind = 'WELDER' },
            @{ TechType = 'LaserCutter'; Anim = 'lasercutter'; Kind = 'LASER_CUTTER' })) {
        Step "Alice uses the $($tool.TechType) twice" {
            $id = Select-Tool $tool.TechType
            $component = $tool.TechType
            $animName = Invoke-Bridge $A "get $id $component.animToolName"
            Wait-HoldsOnBob $animName
            # Alice is under water: the game's underwater effect (index 1)
            $effect = "$id $component.fxControl.emitters[1].instanceGO"
            Invoke-Bridge $A 'set Player.main.armsController.enabled false' | Out-Null
            try {
                foreach ($round in 1, 2) {
                    Use-Tool -Value $true
                    Assert-Playing $tool.Kind "Bob's game plays Alice's $component (round $round)"
                    Assert-ObjectOnBob $effect $true "Bob sees the $component's effect (round $round)"
                    Assert-OnBob "$effect.activeSelf" 'true' "the effect is active (round $round)"
                    if ($component -eq 'Welder') {
                        Assert-OnBob "$id Welder.weldSound.playingSounds" 'true' "Bob's game plays the weld loop (round $round)"
                    }
                    else {
                        Assert-OnBob "$id LaserCutter.fxLight.enabled" 'true' "the cutter's light is on (round $round)"
                        Assert-OnBob "$id LaserCutter.laserCutSound.playingSounds" 'false' "Bob's game doesn't play the relayed cut loop itself (round $round)"
                    }
                    Use-Tool -Value $false
                    Assert-Playing 'NONE' "Bob's game stops Alice's $component (round $round)"
                    Assert-ObjectOnBob $effect $false "the $component's effect is gone (round $round)"
                    if ($component -eq 'Welder') {
                        Assert-OnBob "$id Welder.weldSound.playingSounds" 'false' "the weld loop stops (round $round)"
                    }
                    else {
                        Assert-OnBob "$id LaserCutter.fxLight.enabled" 'false' "the cutter's light is off (round $round)"
                    }
                }
            }
            finally {
                Invoke-Bridge $A 'set Player.main.armsController.enabled true' | Out-Null
            }
        }
    }

    Step 'Alice puts the tool away' {
        Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        $matched = Wait-Until { (Get-Seen 'holding_tool') -eq $false } -TimeoutSeconds 8 -IntervalSeconds 0.25
        Assert-True ([bool]$matched) 'Bob sees Alice put the tool away'
        Assert-Playing 'NONE' "Bob's copy of Alice plays no tool effect"
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'R2c PartnerToolEffects' -Keep:$Keep
