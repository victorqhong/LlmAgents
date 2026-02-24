dotnet pack --configuration Release --output ./nupkg/
dotnet tool uninstall ConsoleAgent -g
dotnet tool install  --tool-path /home/victor/.dotnet/tools/ --source ./nupkg/ ConsoleAgent
