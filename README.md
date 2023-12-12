# thermalCamera
This app is defintely a labor of love. It is meant to view and record two FLIR Lepton thermal camera streams via the PureThermal3 dev board. 
It includes getting temperature from each pixel, so you can mouse over a pixel in the stream and get a temp. 
Recording saves a series of .tiff files to a selectable directory. The files ares saved as 16 bit grey images in their own folder.
Cameras are selectable, so if you are using a laptop you can make sure you're not using you're webcam on accident.
If there are any issues, request, or questions, just make an issue ticket and i'll try to get to it.
Caliberation can be done in a number of ways. one way is to boil water and get the temperature using a thermocouple or just calling it 100C and check the temperature output by the camera, where referenceTemp=100 and cameraReading=(whatever camera is reading). Then you can make a glass of ice water and repeat the process. This will give you two points of calibration. A more accurate method would be to use a PID controlled heater to get a multitude of points across a temperature range. Currently, calibration is not being used but it is in the comments of the .cs file and can be easily enabled. There is also a dropdown menu, but that can be removed since it is only relevent to my current research.

In its current form, the application when compiled must be run as  administrator. There may be a fix for that but I am not good enough at C# to fix that.

![image](https://github.com/nipplefarm/thermalCamera/assets/107640705/3850d7a3-6a99-4f78-9a0e-402b4f5edd0e)
