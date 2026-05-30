@echo off
title D4 Build Filter - Claude Launcher
echo.
echo   Opening the Diablo 4 Build Filter project in Claude...
echo.
echo   - Desktop app : opens Claude; pick the "diablo 4 loot filter" chat from history
echo   - CLI         : opens a terminal from C:\ (so project memory loads) + session picker
echo.

REM 1) Claude DESKTOP app (packaged app, launched by its AppUserModelID).
start "" explorer.exe "shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude"

REM 2) Claude CLI in a new window: cd to C:\ so memory/history load, then the --resume
REM    picker. Choose the "diablo 4 loot filter" session, or Esc for a fresh chat
REM    (memory still loads -- just say "continue the D4 loot filter project").
start "Claude - D4 Filter" cmd /k "cd /d C:\ && claude --resume"

exit
