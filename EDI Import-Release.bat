C:\Windows\Microsoft.NET\Framework\v4.0.30319\msbuild.exe "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import.sln" /p:Configuration=Release
xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Release\EDIImport.exe" "\\Sirus\c$\Program Files\BBS\FOCUS\EDIImport.exe"
xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Release\EDIImport.exe.config" "\\Sirus\c$\Program Files\BBS\FOCUS\EDIImport.exe.config"
xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Release\log4net.dll" "\\Sirus\c$\Program Files\BBS\FOCUS\log4net.dll"
xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Release\CsvHelper.dll" "\\Sirus\c$\Program Files\BBS\FOCUS\CsvHelper.dll"