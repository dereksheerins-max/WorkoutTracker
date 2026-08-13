# WorkoutTracker

A small .NET MAUI workout tracking app with a separate core library and unit tests.

Contents
- WorkoutTracker: MAUI app (UI, ViewModels)
- WorkoutTracker.Core: Core models and services (DB access)
- WorkoutTracker.Tests: xUnit tests for the core database behavior

Prerequisites
- .NET 8 SDK
- For MAUI development: platform-specific workloads (Android/iOS/Windows/mac)

Build & test (CLI)
1. Restore dependencies:
   dotnet restore WorkoutTrackerSolution.sln
2. Build the solution:
   dotnet build WorkoutTrackerSolution.sln --configuration Release
3. Run tests:
   dotnet test WorkoutTracker.Tests/WorkoutTracker.Tests.csproj --configuration Release

CI
A basic GitHub Actions workflow is included at .github/workflows/ci.yml that restores, builds, and runs tests on push and pull requests.

Notes
- Building and packaging the MAUI app for specific platforms requires additional SDKs/workloads (Android SDK/JDK, Xcode for iOS/macOS, Windows Desktop tooling).

License
MIT (add your license text)
