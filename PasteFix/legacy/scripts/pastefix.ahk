#Requires AutoHotkey v2.0
#SingleInstance Force

^+v::
{
    inputFile := A_Temp "\pastefix-input.txt"
    outputFile := A_Temp "\pastefix-output.txt"

    try FileDelete(inputFile)
    try FileDelete(outputFile)

    FileAppend(A_Clipboard, inputFile, "UTF-8-RAW")

    command := A_ComSpec ' /c pastefix.cmd "' inputFile '" > "' outputFile '"'
    RunWait(command, , "Hide")

    if FileExist(outputFile) {
        A_Clipboard := FileRead(outputFile, "UTF-8")

        FileDelete(inputFile)
        FileDelete(outputFile)

        Send("^v")
    }
}