@echo off
Echo #################################################
Echo # Projektordner sauber machen
Echo #################################################
Echo.
taskkill /IM MSBuild.exe /F
taskkill /IM adb.exe /F
taskkill /IM VBCSCompiler.exe /F
RD /S /Q .vs
RD /S /Q TestResults
del /S /F /AH *.suo
del /S /F *.user 
del /S /F *.userprefs
del /S /F *.bak
FOR /D /R %%X IN (bin,obj) DO IF EXIST "%%X" (
Echo Loesche "%%X"
RD /S /Q "%%X"
)
dotnet restore
dotnet restore

