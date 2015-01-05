# 
#  Copyright (c) Microsoft Corporation. All rights reserved. 
#  Licensed under the Apache License, Version 2.0 (the "License");
#  you may not use this file except in compliance with the License.
#  You may obtain a copy of the License at
#  http://www.apache.org/licenses/LICENSE-2.0
#  
#  Unless required by applicable law or agreed to in writing, software
#  distributed under the License is distributed on an "AS IS" BASIS,
#  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
#  See the License for the specific language governing permissions and
#  limitations under the License.
#  

$origdir = (pwd)

cd $PSScriptRoot

if (test-path .\n.exe) {
    erase -force .\n.exe
}

try {
    # get rid of any temp file hanging around
    if (test-path .\n.exe) {
        erase -force .\n.exe
    }

    invoke-webrequest http://nuget.org/nuget.exe -outfile .\n.exe

    $nVer = (Get-Item .\n.exe).VersionInfo.FileVersion

    if( -not $nVer ) {
        write-warning "Downloaded nuget file doesn't appear to be valid"
        if (test-path .\n.exe) {
            erase -force .\n.exe
        }
        cd $origdir
        return
    }

    corflags /32bitreq- .\n.exe
    corflags .\n.exe
    
    if (test-path .\nuget.exe) {
        erase -force .\nuget.exe
    }
    
    ren .\n.exe .\nuget.exe
    
} catch {
   
}

cd $origdir