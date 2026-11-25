// swift-tools-version:5.10
import PackageDescription

// Desktop Buddy, the program: starts the app from DesktopBuddyKit (the folder above).
let package = Package(
    name: "DesktopBuddyV2",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "DesktopBuddyV2", targets: ["DesktopBuddyV2"]),
    ],
    dependencies: [.package(name: "DesktopBuddyKit", path: "..")],
    targets: [
        .executableTarget(name: "DesktopBuddyV2", dependencies: [.product(name: "DesktopBuddyKit", package: "DesktopBuddyKit")]),
    ]
)
