@echo off
rem Opens the Mars Crop Picker window. Double-click this file.
python "%~dp0mars_crop_picker.py" %*
if errorlevel 1 pause
