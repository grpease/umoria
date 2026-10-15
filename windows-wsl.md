# How to Compile, Build, and Run Umoria on Windows with WSL

## 1. Install WSL
Ensure that WSL is installed on your Windows system. You can install it by running the following command in PowerShell:
```powershell
wsl --install
```
This will install WSL along with a default Linux distribution (e.g., Ubuntu).

## 2. Install Required Dependencies
Once WSL is installed, open your Linux terminal and install the necessary dependencies for building the game. Based on the presence of `CMakeLists.txt`, this project likely uses CMake for building. Run the following commands:
```bash
sudo apt update
sudo apt install build-essential cmake git
```

## 3. Clone the Repository (if not already cloned)
If the repository is not already cloned, clone it into your WSL environment:
```bash
git clone https://github.com/dungeons-of-moria/umoria.git
cd umoria
```

## 4. Build the Project
Navigate to the project directory in WSL and create a build directory:
```bash
mkdir build
cd build
cmake ..
make
```
This will configure the project using CMake and compile it using `make`.

## 5. Run the Game
After the build is complete, you can run the game executable. Assuming the executable is named `umoria`:
```bash
./umoria
```

## 6. Access Files from Windows
You can access the files in your WSL environment from Windows using the path:
```
\\wsl$\<DistroName>\home\<YourUsername>\umoria
```
Replace `<DistroName>` and `<YourUsername>` with your Linux distribution name and username.

## 7. Optional: Debugging
If you encounter issues during the build process, check the `README.md` or `CONTRIBUTING.md` files for additional instructions specific to this project.
