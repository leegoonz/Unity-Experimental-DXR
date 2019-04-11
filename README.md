# Unity-Experimental-DXR

This repository contains the Experimental DXR project.

You can clone the repository using the tool of your preference (Git, Github Desktop, Sourcetree, ...). 

  | IMPORTANT                                                    |
  | ------------------------------------------------------------ |
  | This project uses Git Large Files Support (LFS). Downloading a zip file using the green button on Github **will not work**. You must clone the project with a version of git that has LFS. You can download Git LFS here: <https://git-lfs.github.com/>. |

Project zip with all files (including LFS files) can be download from the release section https://github.com/Unity-Technologies/Unity-Experimental-DXR/releases.

The Experimental DXR project is a Unity custom version with binaries based on the 2019.2a5 version of Unity, enhanced with DXR support and version 5.8.0 of HDRP enhanced with DXR support. It is a Windows 10 (64 bit) only version with DX12 API.

This project is a sandbox in which you can  play with real time ray tracing features in Unity. This is a prototype and the final implementation of DXR will be different from this version. This project can not be used to do any production work.

Requirements:
- NVIDIA RTX series card with the latest drivers [here](https://www.nvidia.com/Download/index.aspx?lang=com)
- Windows 10 RS5 (Build 1809) or later


Install step:
Download the project from Github in the release section, unzip.
Launch Unity.exe
Create a new project and select DXR High Definition RP (Preview)

See usage here: https://github.com/Unity-Technologies/Unity-Experimental-DXR/blob/master/documentation/The%20Experimental%20DXR%20project%20manual.pdf

FAQ:
- I get " this application wont work on this computer" when running Unity.exe
You don't have all the files from the repository. This project uses Git Large Files Support (LFS). Downloading a zip file using the green button on Github **will not work**. You must clone the project with a version of git that has LFS. You can download Git LFS here: <https://git-lfs.github.com/>. 

- Windows can't handle more than 260 character for filename. Mean that the project could fail to be created due to long name (we are working on providing a new project with shorter name) Be sure to install file in a short path (like C:\ or D:\)
