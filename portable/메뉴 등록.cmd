@rem Copyright (C) 2026 igedaena
@rem SPDX-License-Identifier: GPL-3.0-or-later
@rem
@rem This file is part of 7-Zip Extension Menu.
@rem This program is free software: you can redistribute it and/or modify
@rem it under the terms of the GNU General Public License as published by
@rem the Free Software Foundation, either version 3 of the License, or
@rem (at your option) any later version.
@rem
@rem This program is distributed in the hope that it will be useful,
@rem but WITHOUT ANY WARRANTY; without even the implied warranty of
@rem MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
@rem or https://www.gnu.org/licenses/ for the complete license.

@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Portable-Menu.ps1" -Action Register
set "menu_result=%errorlevel%"
pause
exit /b %menu_result%
