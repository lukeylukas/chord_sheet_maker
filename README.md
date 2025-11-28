# chord_sheet_maker
Makes chord sheets from MuseScore .mscx files
Uses a lyric file (.txt) to understand line and section divisions. This is crucial for the current version of the program

To build and run:
dotnet build  
dotnet run -- "C:\abs\path\to\file.mscx" "C:\abs\path\to\file.txt"
