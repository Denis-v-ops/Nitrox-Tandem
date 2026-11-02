<#
.SYNOPSIS
    COOP_FEATURES_SPEC R2b: Bob's copy of Alice's body shows her arm and body states: PDA, held tool, tool use,
    swimming at the surface and falling (jump, diving), also after Bob rejoins.

.DESCRIPTION
    Reads the animator parameters of Alice's own body (bridge "animator player" on her game) and of Bob's copy of it
    ("animator <aliceId>" on his game) while Alice:
    - opens and closes the PDA (the game's PDA.Open/Close -> ArmsController.SetUsingPda);
    - draws the scanner, then the builder (QuickSlots.SelectImmediate -> holding_tool from ArmsController.Update); Bob's
      copy also points its hands and arms at the tool as Alice's own arms do (ArmsController.Reconfigure);
    - uses them (using_tool, using_tool_alt, using_builder). Windowless, the game never sets these on the sender: tool use
      needs AvatarInputHandler.IsEnabled(), which is false without a locked cursor (TESTING.md), and no game code calls
      ArmsController.SetUsingBuilder. So the scenario turns Alice's ArmsController off for that step and sets the values on
      her own animator itself; what is tested is her game's watcher, the relay and Bob's copy, not the game's tool input;
    - swims at the surface (on_surface, set by the game's ConditionRules);
    - falls 40 m into open water (jump and diving, set by ArmsController.Update), polled on Bob's game during the fall;
    - then Bob quits and rejoins (windowless only) while Alice floats at the surface with the PDA open (which put her
      scanner away): Bob's new game shows using_pda and on_surface, no tool and no fall; when Alice closes the PDA (the
      game draws the scanner again) it shows holding_tool, holding_scanner and the arms set up for the scanner.
    Before R2b, Bob's copy showed none of these (A0 probe, 2026-11-02), except holding_<tool> from the held-item packet.

    Starts a fresh world and two games, unless -NoLaunch. Run it windowless:
        $env:NITROX_WINDOWLESS = 1; tools\scenarios\R2b-PartnerArms.ps1
#>
param([switch]$NoLaunch, [switch]$Keep, [switch]$NoRejoin, [string]$SaveName = 'r2b-arms')

. (Join-Path $PSScriptRoot 'ScenarioHelpers.ps1')
$A = 'Alice'
$B = 'Bob'
$aliceSpot = @(-60, -15, 380)
$surfaceSpot = @(-60, -0.6, 380)
# Open water away from the escape pod (which floats at the surface): nothing above the sea within the fall's raycast
$dropSpot = @(-160, 40, 300)

function Get-AnimParameters {
    param([string]$Player, [string]$Target)
    (Invoke-BridgeJson $Player "animator $Target")[0].parameters
}

function Get-Own { param([string]$Parameter) (Get-AnimParameters $A 'player').$Parameter }

function Get-Seen { param([string]$Parameter) (Get-AnimParameters $B $script:aliceId).$Parameter }

function Assert-Seen {
    param([string]$Parameter, [bool]$Expected, [string]$Message, [int]$TimeoutSeconds = 8)
    $matched = Wait-Until { (Get-Seen $Parameter) -eq $Expected } -TimeoutSeconds $TimeoutSeconds -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message (Bob's copy of Alice: $Parameter = $(Get-Seen $Parameter))"
}

function Assert-Own {
    param([string]$Parameter, [bool]$Expected, [string]$Message)
    $matched = Wait-Until { (Get-Own $Parameter) -eq $Expected } -TimeoutSeconds 8 -IntervalSeconds 0.25
    Assert-True ([bool]$matched) "$Message (Alice's own body: $Parameter = $(Get-Own $Parameter))"
}

# What ArmsController.Reconfigure set: the hands' IK targets and whether each arm aims
function Get-ArmSetup {
    param([string]$Player, [string]$Target)
    $parts = foreach ($member in 'ik.solver.rightHandEffector.target', 'ik.solver.leftHandEffector.target', 'rightAim.shouldAim', 'leftAim.shouldAim') {
        $value = Invoke-Bridge $Player "get $Target ArmsController.$member"
        "$member=$value"
    }
    ($parts -join '; ') -replace '\(Clone\)', ''
}

function Assert-ArmsMatch {
    param([string]$Message)
    $own = Get-ArmSetup $A 'player'
    $matched = Wait-Until { (Get-ArmSetup $B $script:aliceId) -eq $own } -TimeoutSeconds 8 -IntervalSeconds 0.5
    Assert-True ([bool]$matched) "$Message (Alice's own: $own | Bob's copy: $(Get-ArmSetup $B $script:aliceId))"
}

function Select-Tool {
    param([string]$TechType)
    $id = (Get-InventoryItem $A $TechType | Select-Object -First 1).id
    $slot = [int](Invoke-Bridge $A "call Inventory.main.quickSlots.GetSlotByItem $id")
    Invoke-Bridge $A "call Inventory.main.quickSlots.SelectImmediate $slot" | Out-Null
}

# Alice's arms animator, written by the scenario while her ArmsController is off (see the description)
function Set-OwnArmState {
    param([string]$Parameter, [bool]$Value)
    Invoke-Bridge $A "call Player.main.armsController.animator.SetBool $Parameter $($Value.ToString().ToLowerInvariant())" | Out-Null
}

# Bob quits (in Nitrox that closes the game) and starts again alone, joining the running server
function Restart-Bob {
    Invoke-Bridge $B 'call IngameMenu.main.QuitGame false' | Out-Null
    $left = Wait-Until { -not ((Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B -and $_.online }) } -TimeoutSeconds 60
    if (-not $left) { throw "$B didn't leave the game" }
    $rig = Get-NitroxRig
    $gameDir = if ($rig.GameDir) { $rig.GameDir } else { Find-SubnauticaInstall }
    $dataPathArg = if ([int]$env:NITROX_RIG -gt 0) { " --data-path `"$($rig.DataDir)`"" } else { '' }
    # As dual-client.ps1 starts a windowless game; its process id goes into the rig's state file, so -Stop closes it.
    Wait-Until { -not (Get-Process -Name 'Subnautica' -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match "instantlaunch `"$B`"" }) } -TimeoutSeconds 60 | Out-Null
    $env:NITROX_LAUNCHER_PATH = Join-Path $script:RepoRoot 'Nitrox.Launcher\bin\Debug\net10.0'
    $env:NITROX_HOST_HOME = $env:USERPROFILE
    $env:SteamAppId = '264710'
    $env:SteamGameId = '264710'
    $gameArgs = "-batchmode --instantlaunch `"$B`" --instantlaunch-port $($rig.Port)$dataPathArg -logFile `"$(Join-Path $rig.LogsDir "Player-$B.log")`""
    $game = Start-Process -FilePath (Join-Path $gameDir 'Subnautica.exe') -ArgumentList $gameArgs -WorkingDirectory $gameDir -PassThru
    $state = Get-Content $rig.StateFile -Raw | ConvertFrom-Json
    $state.ProcessIds = @($state.ProcessIds) + $game.Id
    $state | ConvertTo-Json | Set-Content $rig.StateFile
    $joined = Wait-Until { (Invoke-BridgeJson 'server' 'players') | Where-Object { $_.name -eq $B -and $_.online } } -TimeoutSeconds 300 -IntervalSeconds 2
    if (-not $joined) { throw "$B didn't rejoin" }
    Wait-BridgeJoined $B | Out-Null
    Invoke-Bridge $B 'call GameModeUtils.ActivateCheat NoOxygen' | Out-Null
    Invoke-Bridge $B 'set NoDamageConsoleCommand.main.noDamageCheat true' | Out-Null
}

try {
    Start-ScenarioGames -Players $A, $B -SaveName $SaveName -NoLaunch:$NoLaunch
    $script:aliceId = (Invoke-BridgeJson $A 'state').playerId

    Step 'Alice swims 6 m from Bob with a scanner and a builder' {
        Invoke-Bridge $A ('teleport ' + ($aliceSpot -join ' ')) | Out-Null
        Invoke-Bridge $B ("teleport {0} {1} {2}" -f ($aliceSpot[0] + 6), $aliceSpot[1], $aliceSpot[2]) | Out-Null
        foreach ($tool in 'scanner', 'builder') { Invoke-Bridge $A "console item $tool" | Out-Null }
        Assert-True ([bool](Wait-Until { (Get-InventoryItem $A 'Scanner') -and (Get-InventoryItem $A 'Builder') } -TimeoutSeconds 30)) 'Alice has a scanner and a builder'
        Start-Sleep -Seconds 2
        foreach ($parameter in 'using_pda', 'holding_tool', 'using_tool', 'on_surface', 'jump') {
            Assert-Seen $parameter $false "Bob's copy of Alice starts without $parameter"
        }
    }

    Step 'Alice opens the PDA, then closes it' {
        Invoke-Bridge $A 'call Player.main.pda.Open None null null' | Out-Null
        Assert-Own 'using_pda' $true 'the game sets using_pda on Alice'
        Assert-Seen 'using_pda' $true 'Bob sees Alice use the PDA'
        Invoke-Bridge $A 'call Player.main.pda.Close' | Out-Null
        Assert-Seen 'using_pda' $false 'Bob sees Alice put the PDA away'
    }

    Step 'Alice draws the scanner' {
        Select-Tool 'Scanner'
        Assert-Own 'holding_tool' $true 'the game sets holding_tool on Alice'
        Assert-Seen 'holding_tool' $true 'Bob sees Alice hold a tool'
        Assert-Seen 'holding_scanner' $true 'Bob sees the scanner pose (held-item packet)'
        Assert-ArmsMatch "Bob's copy holds and aims the scanner as Alice's own arms do"
    }

    Step 'Alice scans and self-scans (sender values set by the scenario, see the description)' {
        Invoke-Bridge $A 'set Player.main.armsController.enabled false' | Out-Null
        try {
            Set-OwnArmState 'using_tool' $true
            Assert-Seen 'using_tool' $true 'Bob sees Alice use the scanner'
            Set-OwnArmState 'using_tool' $false
            Assert-Seen 'using_tool' $false 'Bob sees Alice stop scanning'
            Set-OwnArmState 'using_tool_alt' $true
            Assert-Seen 'using_tool_alt' $true 'Bob sees Alice scan herself'
            Set-OwnArmState 'using_tool_alt' $false
            Assert-Seen 'using_tool_alt' $false 'Bob sees Alice stop the self-scan'
        }
        finally {
            Invoke-Bridge $A 'set Player.main.armsController.enabled true' | Out-Null
        }
    }

    Step 'Alice draws the builder and builds' {
        Select-Tool 'Builder'
        Assert-Seen 'holding_builder' $true 'Bob sees the builder pose'
        Assert-Seen 'holding_tool' $true 'Bob still sees Alice hold a tool'
        Assert-ArmsMatch "Bob's copy holds the builder as Alice's own arms do"
        Invoke-Bridge $A 'set Player.main.armsController.enabled false' | Out-Null
        try {
            Set-OwnArmState 'using_tool' $true
            Set-OwnArmState 'using_builder' $true
            Assert-Seen 'using_tool' $true 'Bob sees Alice build (the builder is in use)'
            Assert-Seen 'using_builder' $true 'Bob sees using_builder'
            Set-OwnArmState 'using_tool' $false
            Set-OwnArmState 'using_builder' $false
            Assert-Seen 'using_tool' $false 'Bob sees Alice stop building'
        }
        finally {
            Invoke-Bridge $A 'set Player.main.armsController.enabled true' | Out-Null
        }
        Invoke-Bridge $A 'call Inventory.main.quickSlots.DeselectImmediate' | Out-Null
        Assert-Seen 'holding_tool' $false 'Bob sees Alice put the tool away'
        Assert-ArmsMatch "Bob's copy lets go of the tool as Alice's own arms do"
    }

    Step 'Alice swims at the surface' {
        Invoke-Bridge $A ('teleport ' + ($surfaceSpot -join ' ')) | Out-Null
        Assert-Own 'on_surface' $true 'the game sets on_surface on Alice'
        Assert-Seen 'on_surface' $true 'Bob sees Alice swim at the surface'
        Invoke-Bridge $A ('teleport ' + ($aliceSpot -join ' ')) | Out-Null
        Assert-Seen 'on_surface' $false 'Bob sees Alice dive down again'
    }

    Step 'Alice falls 40 m into the sea' {
        Invoke-Bridge $A ('teleport ' + ($dropSpot -join ' ')) | Out-Null
        $seen = @{ jump = $false; diving = $false }
        $samples = 0
        $end = (Get-Date).AddSeconds(8)
        while ((Get-Date) -lt $end) {
            $parameters = Get-AnimParameters $B $script:aliceId
            $samples++
            foreach ($key in @($seen.Keys)) { if ($parameters.$key -eq $true) { $seen[$key] = $true } }
        }
        Write-Host "  $samples samples of Bob's copy during the fall"
        Assert-True $seen['jump'] "Bob sees Alice fall (jump)"
        Assert-True $seen['diving'] "Bob sees Alice dive into the sea (diving)"
        Assert-Own 'is_underwater' $true 'Alice is in the water'
        Assert-Seen 'jump' $false 'the fall pose ends in the water'
        Assert-Seen 'diving' $false 'the dive pose ends in the water'
    }

    if ($NoRejoin -or -not ($env:NITROX_WINDOWLESS -in @('1', 'true'))) {
        Write-Host 'Skipping the rejoin (it starts a single windowless game; -NoRejoin or not windowless)' -ForegroundColor Yellow
    }
    else {
        Step 'Alice floats at the surface with the scanner and the PDA open; Bob rejoins' {
            Invoke-Bridge $B ('teleport ' + ($aliceSpot -join ' ')) | Out-Null
            Invoke-Bridge $A ('teleport ' + ($surfaceSpot -join ' ')) | Out-Null
            Select-Tool 'Scanner'
            Invoke-Bridge $A 'call Player.main.pda.Open None null null' | Out-Null
            Assert-Own 'on_surface' $true 'Alice is at the surface'
            Assert-Own 'using_pda' $true 'Alice has the PDA open'
            # PDA.Open puts the held tool away (Inventory.ReturnHeld) and PDA.Close draws it again
            Assert-Own 'holding_scanner' $false 'opening the PDA put the scanner away'
            Assert-Own 'holding_tool' $false 'with the PDA open Alice holds no tool'
            Restart-Bob
            Assert-Seen 'using_pda' $true "Bob's new game shows Alice using the PDA" 20
            Assert-Seen 'on_surface' $true "Bob's new game shows Alice at the surface"
            Assert-Seen 'holding_tool' $false "Bob's new game shows no tool in Alice's hand"
            Assert-True (-not (Get-Seen 'jump') -and -not (Get-Seen 'diving')) "Bob's new game shows no fall"
        }

        Step 'Alice closes the PDA after the rejoin' {
            Invoke-Bridge $A 'call Player.main.pda.Close' | Out-Null
            Assert-Seen 'using_pda' $false 'Bob sees Alice put the PDA away'
            Assert-Seen 'holding_tool' $true 'Bob sees Alice hold a tool again'
            Assert-Seen 'holding_scanner' $true 'Bob sees the scanner pose again'
            Assert-ArmsMatch "after the rejoin, Bob's copy holds the scanner as Alice's own arms do"
        }
    }
}
catch {
    if ($script:ScenarioFailures.Count -eq 0) { Add-Failure $_.Exception.Message }
}
Complete-Scenario -Name 'R2b PartnerArms' -Keep:$Keep
