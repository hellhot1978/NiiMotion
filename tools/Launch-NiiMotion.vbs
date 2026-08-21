Option Explicit

Dim shell
Set shell = CreateObject("WScript.Shell")

shell.CurrentDirectory = "C:\NiirMotion"
shell.Run """C:\NiirMotion\artifacts\app\NiiRMotion.App.exe""", 0, False
