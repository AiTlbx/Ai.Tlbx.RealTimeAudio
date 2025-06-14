@echo off
echo ##################################################################
echo # Clean in Between™️- Keeping it fresh down there since 2023 #
echo ##################################################################
echo -
taskkill /IM MSBuild.exe /F 
taskkill /IM VBCSCompiler.exe /F 
RD /S /Q TestResults 
del /S /F *.bak 

:: Durchlaufen aller Ordner und Unterordner und Löschen von "bin" und "obj", wenn sie existieren
FOR /R %%X IN (bin,obj) DO (
    IF EXIST "%%X" (
        echo Loesche "%%X"
        RD /S /Q "%%X"
    )
)

:: DotNet Wiederherstellung
echo -
echo Script erfolgreich ausgefuehrt.