<#
.SYNOPSIS
    A0 probe run (docs/fork/COOP_FEATURES_SPEC.md, wave 0): answers the (H) marks that only the game can answer.

.DESCRIPTION
    Observations, not assertions: each probe prints what it found and adds it to $OutDir\a0-findings.json. A probe that
    throws is recorded as an error and the run goes on; the run FAILs only if a probe could not run. The findings were
    copied into the spec items by hand (COOP_FEATURES_SPEC.md, "A0 probe" notes).

    Probes (-Probes picks some): D2 (biome strings at known spots), B5 (wild fruit regrowth), D1 (partner's breathing,
    bubbles and pain sounds), R2 (partner body's animator while scanning, building, PDA), T1 (player_death trigger on the
    partner's body), D6 (partner's lights at night at 20/60/120 m: light state, no screenshots, since a windowless
    screenshot at night crashed a game), B4 (Aurora fire parents), T4 (standing player vs Cyclops helm HUD).
    F2 and P2 aren't here: F2 was answered from the game code, P2 needs a base with an aquarium (left to P2).

    Starts a fresh world (save a0-probes) and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\A0-Probes.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [string]$SaveName = 'a0-probes',
    [string[]]$Probes = @('D2', 'B5', 'D1', 'R2', 'T1', 'D6', 'B4', 'T4'),
    [string]$OutDir = (Join-Path $env:TEMP 'a0-probes'))

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$findingsFile = Join-Path $OutDir 'a0-findings.json'
$script:Findings = [ordered]@{}
if (Test-Path $findingsFile) {
    (Get-Content $findingsFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $script:Findings[$_.Name] = $_.Value }
}

function Save-Finding {
    param([string]$Probe, $Value)
    $script:Findings[$Probe] = $Value
    $script:Findings | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $findingsFile
    Write-Host "  finding $Probe saved" -ForegroundColor Green
}

function Fmt {
    param([double[]]$P)
    ($P | ForEach-Object { $_.ToString('0.###') }) -join ' '
}

function Get-State { param([string]$Player) Invoke-BridgeJson $Player 'state' }

function Get-PlayerId { param([string]$Player) (Get-State $Player).playerId }

function Move-To {
    param([string]$Player, [double[]]$Position, [double]$SettleSeconds = 3)
    Invoke-Bridge $Player ('teleport ' + (Fmt $Position)) | Out-Null
    Start-Sleep -Milliseconds ([int]($SettleSeconds * 1000))
}

function Get-Biome {
    param([string]$Player)
    $pos = [double[]](Get-State $Player).position
    [ordered]@{
        position     = $pos
        biome        = Invoke-Bridge $Player 'call player Player.GetBiomeString'
        override     = Invoke-Bridge $Player 'call AtmosphereDirector.main.GetBiomeOverride'
        largeWorld   = Invoke-Bridge $Player ('call LargeWorld.main.GetBiome ' + (($pos | ForEach-Object { $_.ToString('0.###') }) -join ','))
        depth        = [math]::Round(-$pos[1], 1)
        precursorOOW = (Get-State $Player).precursorOutOfWater
    }
}

function Invoke-Probe {
    param([string]$Name, [scriptblock]$Body)
    if ($Probes -notcontains $Name) { return }
    Write-Host ''
    Write-Host "== Probe $Name" -ForegroundColor Cyan
    $script:ScenarioChecks++
    try {
        & $Body
    }
    catch {
        Add-Failure "$Name threw: $($_.Exception.Message)"
        Save-Finding $Name ([ordered]@{ error = $_.Exception.Message })
    }
}

Start-ScenarioGames -SaveName $SaveName -NoLaunch:$NoLaunch
$aId = Get-PlayerId $A
$bId = Get-PlayerId $B
Write-Host "Alice $aId, Bob $bId"

# --- D2: real biome strings at known spots (teleport targets from GotoConsoleCommand.main.data.locations) ---
Invoke-Probe 'D2' {
    $spots = [ordered]@{
        'Safe Shallows (pod)'                 = @(-60, -12, 380)
        'Kelp Forest'                         = @(75, -28, -95)
        'Grassy Plateaus'                     = @(263, -90, 64)
        'Jellyshroom Cave (Degasi base2)'     = @(114, -258, -372)
        'Deep Grand Reef (Degasi base3)'      = @(-650, -503, -951)
        'Floating Island (base1)'             = @(-756, 22, -1111)
        'Mushroom Forest teleporter'          = @(-802, -226, 409)
        'Disease Research Facility'           = @(-222, -791, 320)
        'Lost River teleporter'               = @(-942.7, -622.3, 1021.95)
        'Inactive Lava Zone: Thermal Plant'   = @(-32, -1201, 66)
        'Thermal Plant inside'                = @(-31, -1204, 87)
        'Primary Containment (prison)'        = @(203, -1447, -235)
        'Prison aquarium'                     = @(320, -1466, -436)
        'Gun inside'                          = @(452, -71, 1197)
        'Aurora'                              = @(1157, 3.5, 113)
        'Crag Field teleporter'               = @(-80.871, -290.192, -1359.329)
        'Lifepod 29 (-500 m)'                 = @(-485, -500, 1326)
    }
    $result = [ordered]@{}
    foreach ($spot in $spots.Keys) {
        Move-To $A $spots[$spot] 4
        $biome = Get-Biome $A
        Write-Host ("  {0,-38} biome={1} override={2} map={3} depth={4}" -f $spot, $biome.biome, $biome.override, $biome.largeWorld, $biome.depth)
        $result[$spot] = $biome
    }
    # Every biome name the game knows (fog/sky settings), for the interference list
    $settings = Invoke-Bridge $A 'get WaterBiomeManager.main.biomeSettings'
    $result['allBiomeNames'] = @([regex]::Matches($settings, '"name":"([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    Save-Finding 'D2' $result
}

# --- B5: do wild fruit plants regrow? ---
Invoke-Probe 'B5' {
    $result = [ordered]@{}
    foreach ($spot in @(@{ Name = 'creepvines near the pod'; Pos = @(-60, -12, 380) }, @{ Name = 'lantern trees, Floating Island'; Pos = @(-756, 22, -1111) })) {
        Move-To $A $spot.Pos 5
        $plants = @(Invoke-BridgeJson $A 'find FruitPlant 60') | Where-Object { $_.id }
        $states = foreach ($plant in $plants) {
            $fp = Invoke-BridgeJson $A "get $($plant.id) FruitPlant"
            if ($fp.fruits.Count -gt 0) { [ordered]@{ techType = $plant.techType; fruits = $fp.fruits.Count; fruitSpawnEnabled = $fp.fruitSpawnEnabled; timeNextFruit = $fp.timeNextFruit; interval = $fp.fruitSpawnInterval } }
        }
        $states = @($states)
        $enabled = @($states | Where-Object { $_.fruitSpawnEnabled }).Count
        Write-Host "  $($spot.Name): $($states.Count) plants with fruit, $enabled with fruitSpawnEnabled"
        $result[$spot.Name] = [ordered]@{ plantsWithFruit = $states.Count; spawnEnabled = $enabled; sample = $states | Select-Object -First 3 }
    }
    Save-Finding 'B5' $result
}

# --- D1: does the partner's breathing / bubbles / pain play on their body? ---
Invoke-Probe 'D1' {
    Move-To $A @(-60, -15, 380) 0
    Move-To $B @(-54, -15, 380) 3
    if (-not (Get-State $A).equipment.Tank) {
        Invoke-Bridge $A 'console item tank' | Out-Null   # equips itself into the free tank slot
        Wait-Until { (Get-State $A).equipment.Tank } -TimeoutSeconds 10 | Out-Null
    }
    Invoke-Bridge $A 'sounds clear' | Out-Null
    Invoke-Bridge $B 'sounds clear' | Out-Null
    # Out of the water and back in restarts the breathing loop (BreathingSound.UpdateSound -> StartEvent)
    Move-To $A @(-60, 3, 380) 2
    Move-To $A @(-60, -15, 380) 12
    Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat false' | Out-Null
    Invoke-Bridge $A 'call player LiveMixin.TakeDamage 10 0,0,0 Normal null' | Out-Null
    Start-Sleep 1
    Invoke-Bridge $A 'call player LiveMixin.TakeDamage 4 0,0,0 Normal null' | Out-Null
    Invoke-Bridge $A 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
    Start-Sleep 2
    $isPlayerSound = { $_.path -match 'event:/player/' }
    $local = @(Invoke-BridgeJson $A 'sounds 30' | Where-Object { $_.localPlayer -and (& $isPlayerSound) } | ForEach-Object { "$($_.source) $($_.path)" } | Sort-Object -Unique)
    $partner = @(Invoke-BridgeJson $B 'sounds 30' | Where-Object { $_.remotePlayer -eq $A } | ForEach-Object { "$($_.source) $($_.path) dist=$($_.distance)" } | Sort-Object -Unique)
    $paths = [ordered]@{}
    foreach ($field in 'painWithTank', 'painWithoutTank', 'painWithoutMask', 'painSmoke') { $paths[$field] = Invoke-Bridge $A "get find:PlayerDamageSounds PlayerDamageSounds.$field.asset.path" }
    $paths['painWithoutTankLight'] = Invoke-Bridge $A 'get find:PlayerDamageSounds PlayerDamageSounds.painWithoutTankLight.Event'
    $paths['breathing'] = Invoke-Bridge $A 'get find:BreathingSound BreathingSound.loopingBreathingSound.asset.path'
    $paths['bubbles'] = Invoke-Bridge $A 'get find:PlayerBreathBubbles PlayerBreathBubbles.bubbleSound.asset.path'
    Write-Host "  Alice's own player sounds: $($local -join '; ')"
    Write-Host "  played on Alice's body in Bob's game: $($partner -join '; ')"
    Save-Finding 'D1' ([ordered]@{ aliceLocal = $local; onAliceBodyForBob = $partner; emitterPaths = $paths })
}

# --- R2: the partner body's animator values while A opens the PDA, scans, holds the builder ---
Invoke-Probe 'R2' {
    $keys = 'using_pda', 'using_tool', 'using_tool_alt', 'using_tool_first', 'using_builder', 'holding_tool', 'holding_scanner', 'holding_builder', 'is_underwater', 'on_surface', 'diving', 'jump'
    function Read-Anim([string]$Player, [string]$Target) {
        $parameters = (Invoke-BridgeJson $Player "animator $Target")[0].parameters
        $values = [ordered]@{}
        foreach ($key in $keys) { $values[$key] = $parameters.$key }
        $values
    }
    function Compare-Anim([string]$Label) {
        $mine = Read-Anim $A 'player'
        $seen = Read-Anim $B $aId
        $differs = @($keys | Where-Object { "$($mine[$_])" -ne "$($seen[$_])" })
        Write-Host "  $Label : Alice's game $(($keys | Where-Object { $mine[$_] -eq $true }) -join ',') | Bob sees $(($keys | Where-Object { $seen[$_] -eq $true }) -join ',')"
        [ordered]@{ alice = $mine; bobSees = $seen; differs = $differs }
    }
    Move-To $A @(-60, -15, 380) 0
    Move-To $B @(-54, -15, 380) 3
    foreach ($tool in 'scanner', 'builder') { Invoke-Bridge $A "console item $tool" | Out-Null }
    Start-Sleep 3
    $inventory = (Get-State $A).inventory
    $slot = @{}
    foreach ($tool in 'Scanner', 'Builder') {
        $id = ($inventory | Where-Object techType -eq $tool | Select-Object -First 1).id
        $slot[$tool] = [int](Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $id")
    }
    $result = [ordered]@{}
    Invoke-Bridge $A 'call Player.main.pda.Open None null null' | Out-Null
    Start-Sleep 1.5
    $result['pdaOpen'] = Compare-Anim 'PDA open'
    Invoke-Bridge $A 'call Player.main.pda.Close' | Out-Null
    Start-Sleep 1
    Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $($slot['Scanner'])" | Out-Null
    Start-Sleep 2
    $result['scannerDrawn'] = Compare-Anim 'scanner drawn'
    Invoke-Bridge $A 'hold none 3 RightHand' | Out-Null
    Start-Sleep 1
    $result['scannerTriggerHeld'] = Compare-Anim 'scanner, right hand held'
    Start-Sleep 3
    Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $($slot['Builder'])" | Out-Null
    Start-Sleep 2
    $result['builderDrawn'] = Compare-Anim 'builder drawn'
    Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
    Save-Finding 'R2' $result
}

# --- T1: does the player_death trigger on the partner's body play, and does it hold? ---
Invoke-Probe 'T1' {
    $result = [ordered]@{ cullingMode = (Invoke-BridgeJson $B "animator $aId")[0].cullingMode }
    foreach ($trigger in 'player_death', 'player_death_drown') {
        Invoke-Bridge $B "call $aId Animator.SetTrigger $trigger" | Out-Null
        $samples = for ($i = 0; $i -lt 6; $i++) {
            Start-Sleep -Milliseconds 600
            $layer = (Invoke-BridgeJson $B "animator $aId")[0].layers | Where-Object layer -eq 'Death'
            "t=$([math]::Round(($i + 1) * 0.6, 1))s $($layer.clips -join ',') ($($layer.normalizedTime))"
        }
        Write-Host "  $trigger : $($samples -join ' | ')"
        $result[$trigger] = @($samples)
        Start-Sleep 2
    }
    Save-Finding 'T1' $result
}

# --- D6: the partner's lights at night (light state on B; screenshots only with -Screenshots, see TESTING.md) ---
Invoke-Probe 'D6' {
    Invoke-Bridge 'server' 'time night' | Out-Null
    $result = [ordered]@{}
    $vmLayer = [int](Invoke-Bridge $B 'call LayerMask.NameToLayer Viewmodel')
    $mask = [int64](Invoke-Bridge $B 'get MainCamera.camera.cullingMask')
    $result['mainCameraRendersViewmodelLayer'] = (($mask -band (1L -shl $vmLayer)) -ne 0)
    $result['isDay'] = Invoke-Bridge $B 'call DayNightCycle.main.IsDay'
    function Read-Light([string]$Player, [string]$Id) {
        $state = [ordered]@{ lightsActive = Invoke-Bridge $Player "get $Id ToggleLights.lightsActive" -AllowError }
        foreach ($member in 'enabled', 'intensity', 'range') { $state[$member] = Invoke-Bridge $Player "get $Id Light.$member" -AllowError }
        $state['layer'] = Invoke-Bridge $Player "get $Id GameObject.layer"
        $state['active'] = (Invoke-BridgeJson $Player "entity $Id").active
        $state
    }
    Move-To $B @(263, -60, 64) 0
    Move-To $A @(263, -60, 84) 4
    foreach ($tool in 'flashlight', 'seaglide') {
        if (-not ((Get-State $A).inventory | Where-Object techType -eq $tool)) { Invoke-Bridge $A "console item $tool" | Out-Null; Start-Sleep 3 }
    }
    $inventory = (Get-State $A).inventory
    $flashlight = ($inventory | Where-Object techType -eq 'Flashlight' | Select-Object -First 1).id
    $seaglide = ($inventory | Where-Object techType -eq 'Seaglide' | Select-Object -First 1).id
    $flare = ($inventory | Where-Object techType -eq 'Flare' | Select-Object -First 1).id
    # Selecting the slot that is already drawn would put the tool away
    function Draw([string]$Id) { if ((Get-State $A).held -eq $Id) { return }; Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $(Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $Id")" | Out-Null; Start-Sleep 2 }
    function Holster { Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null; Start-Sleep 1 }
    foreach ($distance in 20, 60, 120) {
        Move-To $A @(263, -60, (64 + $distance)) 4
        Draw $flashlight
        $result["flashlight $distance m"] = [ordered]@{ alice = Read-Light $A $flashlight; bobSees = Read-Light $B $flashlight }
        Write-Host "  flashlight at $distance m: Alice $($result["flashlight $distance m"].alice | ConvertTo-Json -Compress) | Bob $($result["flashlight $distance m"].bobSees | ConvertTo-Json -Compress)"
    }
    # A light switched while the tool is put away, then drawn
    Holster
    Invoke-Bridge $A "call $flashlight ToggleLights.SetLightsActive false" | Out-Null
    Start-Sleep 1
    Draw $flashlight
    $result['flashlight switched off while holstered, then drawn'] = [ordered]@{ alice = Read-Light $A $flashlight; bobSees = Read-Light $B $flashlight }
    Holster
    Invoke-Bridge $A "call $flashlight ToggleLights.SetLightsActive true" | Out-Null
    Start-Sleep 1
    Draw $flashlight
    $result['flashlight switched on while holstered, then drawn'] = [ordered]@{ alice = Read-Light $A $flashlight; bobSees = Read-Light $B $flashlight }
    Write-Host "  switched while holstered: $($result['flashlight switched off while holstered, then drawn'] | ConvertTo-Json -Compress -Depth 3)"
    Write-Host "  switched while holstered: $($result['flashlight switched on while holstered, then drawn'] | ConvertTo-Json -Compress -Depth 3)"
    Draw $seaglide
    $result['seaglide 120 m'] = [ordered]@{ alice = Read-Light $A $seaglide; bobSees = Read-Light $B $seaglide }
    Write-Host "  seaglide: $($result['seaglide 120 m'] | ConvertTo-Json -Compress -Depth 3)"
    # A thrown flare: draw it, press the right hand (throw), then read the world flare in both games
    Draw $flare
    Invoke-Bridge $A 'hold none 0.3 RightHand' | Out-Null
    Start-Sleep 2
    # The throw itself comes from an animation event (Flare.OnToolUseAnim), which a windowless game doesn't fire
    if ((Invoke-Bridge $A "get $flare Flare.hasBeenThrown" -AllowError) -ne 'true') {
        Invoke-Bridge $A "call $flare Flare.OnToolUseAnim player" | Out-Null
    }
    Start-Sleep 5
    $thrown = [ordered]@{ alice = [ordered]@{ active = Invoke-Bridge $A "get $flare Flare.flareActiveState" -AllowError; light = Invoke-Bridge $A "get $flare Light.enabled" -AllowError; position = (Invoke-BridgeJson $A "entity $flare").position }
                          bobSees = [ordered]@{ active = Invoke-Bridge $B "get $flare Flare.flareActiveState" -AllowError; light = Invoke-Bridge $B "get $flare Light.enabled" -AllowError; position = (Invoke-BridgeJson $B "entity $flare").position } }
    $result['flare thrown at 120 m'] = $thrown
    Write-Host "  flare: $($thrown | ConvertTo-Json -Compress -Depth 3)"
    Holster
    Invoke-Bridge 'server' 'time day' | Out-Null
    Save-Finding 'D6' $result
}

# --- B4: does an Aurora fire's parent have a Nitrox id? ---
Invoke-Probe 'B4' {
    # The aurora command must come from a player (RequiresOrigin PLAYER)
    Invoke-Bridge $A 'chat /aurora explode' | Out-Null
    Start-Sleep 20
    $result = [ordered]@{}
    foreach ($spot in @(@{ Name = 'cargoroom'; Pos = @(940, 14, 18) }, @{ Name = 'genroom'; Pos = @(884, 14, -13) }, @{ Name = 'exoroom'; Pos = @(990, 4, -16) })) {
        Move-To $A $spot.Pos 6
        $fires = @(Invoke-BridgeJson $A 'find Fire 40') | Where-Object { $_.distance -lt 60 }
        # id: the fire's own NitroxId, or "(in <id>)" for the nearest ancestor that has one
        $described = foreach ($fire in $fires | Select-Object -First 6) {
            $ancestor = if ($fire.id -match '^\(in (.+)\)$') { $Matches[1] } else { $null }
            [ordered]@{
                name         = $fire.name
                id           = $fire.id
                distance     = $fire.distance
                ancestorName = if ($ancestor) { (Invoke-BridgeJson $A "entity $ancestor").name } else { $null }
            }
        }
        if ($fires.Count -gt 0) {
            $result["$($spot.Name) nearest fire's direct parent"] = Invoke-Bridge $A 'get find:Fire Transform.parent.name' -AllowError
        }
        Write-Host "  $($spot.Name): $($fires.Count) fires; $(@($described | ForEach-Object { "$($_.name) id=$($_.id) ancestor=$($_.ancestorName)" }) -join '; ') | nearest's parent: $($result["$($spot.Name) nearest fire's direct parent"])"
        $result[$spot.Name] = @($described)
    }
    Save-Finding 'B4' $result
}

# --- T4: can a standing (non-piloting) player in a Cyclops reach the helm HUD buttons? ---
Invoke-Probe 'T4' {
    $spawn = @(263, -60, 64)
    Move-To $A $spawn 3
    Invoke-Bridge $A 'look 0 0' | Out-Null
    Invoke-Bridge $A 'console sub cyclops' | Out-Null
    $cyclops = Wait-Until { @(Invoke-BridgeJson $A 'find SubRoot') | Where-Object { $_.name -like 'Cyclops*' -and $_.id -match '^[0-9a-f-]{36}$' } | Select-Object -First 1 } -TimeoutSeconds 60
    if (-not $cyclops) { throw 'no Cyclops spawned' }
    Start-Sleep 5
    $inside = $false
    for ($attempt = 0; $attempt -lt 3 -and -not $inside; $attempt++) {
        $hatch = Wait-Until { (Invoke-BridgeJson $A 'find CinematicModeTrigger 40') | Where-Object { $_.name -eq 'EntranceHatch' -and $_.id -eq "(in $($cyclops.id))" } | Select-Object -First 1 } -TimeoutSeconds 20
        Move-To $A @($hatch.position[0], ($hatch.position[1] - 6.9), $hatch.position[2]) 1
        Invoke-Bridge $A 'use find:CinematicModeTrigger#EntranceHatch' | Out-Null
        $inside = [bool](Wait-Until { (Get-State $A).currentSub.id -eq $cyclops.id } -TimeoutSeconds 15)
    }
    Wait-Until { (Invoke-Bridge $A 'get player Player.cinematicModeActive') -eq 'false' } -TimeoutSeconds 20 | Out-Null
    Start-Sleep 2
    $chair = (Invoke-BridgeJson $A 'find PilotingChair 3') | Select-Object -First 1
    $hud = (Invoke-BridgeJson $A 'find CyclopsHelmHUDManager 3') | Select-Object -First 1
    $result = [ordered]@{ insideCyclops = [bool]$inside; chair = $chair.position; hud = $hud.position
        raycasterInteractionDistance = Invoke-Bridge $A 'get find:CyclopsHelmHUDManager uGUI_GraphicRaycaster.interactionDistance' -AllowError
        raycasterHasStatusDelegate   = (Invoke-Bridge $A 'get find:CyclopsHelmHUDManager uGUI_GraphicRaycaster.updateRaycasterStatusDelegate' -AllowError) -ne 'null'
    }
    # Stand where a co-pilot would: 1 m and 2 m behind the chair (away from the HUD), same height as the chair
    $dx = $chair.position[0] - $hud.position[0]; $dz = $chair.position[2] - $hud.position[2]
    $length = [math]::Sqrt($dx * $dx + $dz * $dz)
    foreach ($back in 0.8, 1.5, 2.5) {
        $spot = @(($chair.position[0] + $dx / $length * $back), ($chair.position[1] + 0.2), ($chair.position[2] + $dz / $length * $back))
        Move-To $A $spot 2
        $state = Get-State $A
        $hudNow = (Invoke-BridgeJson $A 'find CyclopsHelmHUDManager 1') | Select-Object -First 1
        $result["standing $back m behind the chair"] = [ordered]@{
            playerToHud        = $hudNow.distance
            currentSubIsCyclops = $state.currentSub.id -eq $cyclops.id
            raycasterEnabled   = Invoke-Bridge $A 'get find:CyclopsHelmHUDManager uGUI_GraphicRaycaster.enabled' -AllowError
            canvasInteractable = Invoke-Bridge $A 'get find:CyclopsHelmHUDManager CyclopsHelmHUDManager.canvasGroup.interactable' -AllowError
            hudActive          = Invoke-Bridge $A 'get find:CyclopsHelmHUDManager CyclopsHelmHUDManager.hudActive' -AllowError
        }
        Write-Host "  $back m behind the chair: $($result["standing $back m behind the chair"] | ConvertTo-Json -Compress)"
    }
    Save-Finding 'T4' $result
}

Complete-Scenario -Name 'A0-Probes' -Keep:$Keep
