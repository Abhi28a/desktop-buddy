// swift-tools-version:5.10
import PackageDescription

// The app's code and screens, as a library. Open this folder in Xcode to see live previews.
// The program itself is in App/ (one line that starts the app); build-app.sh builds it.
let package = Package(
    name: "DesktopBuddyKit",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "DesktopBuddyKit", targets: ["DesktopBuddyKit"]),
    ],
    targets: [
        .target(name: "DesktopBuddyKit", path: "Sources/DesktopBuddy", resources: [.copy("Logos")]),
    ]
)
