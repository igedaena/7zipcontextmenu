# Copyright (C) 2026 igedaena
# SPDX-License-Identifier: GPL-3.0-or-later
#
# This file is part of 7-Zip Extension Menu.
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
# or https://www.gnu.org/licenses/ for the complete license.

cmake_minimum_required(VERSION 3.25)
project(SevenZipContextMenu VERSION 0.4.1 LANGUAGES CXX RC)

if(NOT WIN32 OR NOT MSVC)
    message(FATAL_ERROR "This project requires Windows and MSVC.")
endif()

add_executable(sdk_probe tools/sdk_probe.cpp)
target_compile_features(sdk_probe PRIVATE cxx_std_20)
target_compile_definitions(sdk_probe PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(sdk_probe PRIVATE /W4 /WX /utf-8 /permissive-)
target_link_libraries(sdk_probe PRIVATE ole32 shell32 uuid)
set_property(TARGET sdk_probe PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

function(configure_native target)
    target_compile_features(${target} PRIVATE cxx_std_20)
    target_compile_definitions(${target} PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_compile_options(${target} PRIVATE /W4 /WX /utf-8 /permissive-)
    set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endfunction()

configure_file(src/shell/Resources.rc.in "${CMAKE_CURRENT_BINARY_DIR}/Resources.rc" @ONLY)
add_library(SevenZipContextMenu SHARED
    src/shell/ShellExtension.cpp src/shell/MenuModel.cpp src/shell/ShellExtension.def "${CMAKE_CURRENT_BINARY_DIR}/Resources.rc")
configure_native(SevenZipContextMenu)
target_link_libraries(SevenZipContextMenu PRIVATE ole32 shell32 user32 gdi32 advapi32 uuid)

set(CSC "$ENV{SystemRoot}/Microsoft.NET/Framework64/v4.0.30319/csc.exe")
if(NOT EXISTS "${CSC}")
    message(FATAL_ERROR "The Windows .NET Framework C# compiler is required.")
endif()
set(WORKER "${CMAKE_CURRENT_BINARY_DIR}/$<CONFIG>/SevenZipContextWorker.exe")
string(REPLACE "/" "\\" WORKER_NATIVE "${WORKER}")
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/app.manifest" MANIFEST_NATIVE)
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/Worker.cs" SOURCE_NATIVE)
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/Settings.cs" SETTINGS_NATIVE)
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/NativeCommands.cs" NATIVE_COMMANDS_NATIVE)
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/RuntimeSafety.cs" SAFETY_NATIVE)
file(TO_NATIVE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/assets/sevenzip-extension.ico" ICON_NATIVE)
add_custom_command(OUTPUT "${WORKER}" "${WORKER}.config"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/$<CONFIG>"
    COMMAND ${CMAKE_COMMAND} -E env --unset=LIB "${CSC}" /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll
        /r:System.Core.dll /r:System.Xml.dll
        "/win32manifest:${MANIFEST_NATIVE}"
        "/win32icon:${ICON_NATIVE}"
        "/out:${WORKER_NATIVE}" "${SOURCE_NATIVE}" "${SETTINGS_NATIVE}" "${NATIVE_COMMANDS_NATIVE}" "${SAFETY_NATIVE}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_CURRENT_SOURCE_DIR}/src/worker/App.config" "${WORKER}.config"
    DEPENDS src/worker/Worker.cs src/worker/Settings.cs src/worker/NativeCommands.cs src/worker/RuntimeSafety.cs src/worker/app.manifest src/worker/App.config assets/sevenzip-extension.ico VERBATIM)
add_custom_target(worker ALL DEPENDS "${WORKER}" "${WORKER}.config")

add_executable(shell_probe tools/shell_probe.cpp)
configure_native(shell_probe)
target_include_directories(shell_probe PRIVATE src/shell)
target_link_libraries(shell_probe PRIVATE ole32 shell32 user32 advapi32 uuid)

add_executable(host_menu_probe tools/host_menu_probe.cpp)
configure_native(host_menu_probe)
target_link_libraries(host_menu_probe PRIVATE ole32 shell32 user32 uuid)
