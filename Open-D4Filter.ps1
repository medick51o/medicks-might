# Open the Diablo 4 Build Filter project in Claude — both the CLI and the desktop app.
#
# Why it launches the CLI from C:\ : Claude keys its project MEMORY and CHAT HISTORY to
# the directory it was launched from. All of this project's history lives under the C:\
# launch dir, so launching from anywhere else starts a blank slate. Project CODE lives at
# C:\Sync\Projects\D4BuildFilter — memory points there.

$ErrorActionPreference = 'SilentlyContinue'

Write-Host "Opening the D4 Build Filter project in Claude..." -ForegroundColor Cyan

# 1) Claude DESKTOP app (a packaged app — launched by its AppUserModelID).
#    Opens the app; pick the "diablo 4 loot filter" chat from history to continue.
Start-Process 'explorer.exe' 'shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude'

# 2) Claude CLI in a new terminal, working dir = C:\ so memory loads, then show the
#    session picker. Choose the "diablo 4 loot filter" session to resume exactly,
#    or press Esc for a fresh chat (memory still loads — just say "continue the D4
#    loot filter project").
Start-Process 'powershell.exe' -ArgumentList @(
    '-NoExit',
    '-Command',
    'Set-Location C:\; Write-Host "Claude — D4 Build Filter (launched from C:\ so memory loads)" -ForegroundColor Green; claude --resume'
)
