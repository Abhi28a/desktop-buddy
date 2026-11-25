import SwiftUI

// Live previews for Xcode's design canvas. Open the package in Xcode, pick a screen file or this one,
// and press ⌥⌘↩ to show the canvas. Previews use made-up sample data in a throwaway folder,
// never your real settings or sign-ins.

private let previewWallpaper = LinearGradient(
    colors: [Color(red: 0.55, green: 0.68, blue: 0.85), Color(red: 0.85, green: 0.78, blue: 0.9)],
    startPoint: .top, endPoint: .bottom)

#Preview("Notification list") {
    SampleData.loadAll()
    return DockListView().padding(20).background(previewWallpaper)
}

#Preview("Dock: bottom right") {
    SampleData.loadAll()
    AppState.shared.config.dockSpot = .bottomRight
    return DockView(model: DockModel())
        .frame(width: 600, height: 160)
        .background(previewWallpaper)
}

#Preview("Menu bar dropdown") {
    SampleData.loadAll()
    return MenuBarPopover(openSettings: {})
}

#Preview("Menu bar pop-up") {
    SampleData.loadAll()
    let model = GlimpseModel.sample
    model.phase = .open
    return GlimpseView(model: model, actions: GlimpseActions(open: {}, done: {}, close: {}, viewMore: {}))
        .frame(width: GlimpsePlacement.canvas.width, height: GlimpsePlacement.canvas.height)
        .background(previewWallpaper)
}

#Preview("Settings") {
    SampleData.loadAccounts()
    return SettingsRoot().frame(width: 840, height: 620)
}

#Preview("Welcome tour") {
    TourView(finish: { _ in })
}

// Tough backgrounds: the glass is lightest over a white window, so text contrast is checked here.
#Preview("Notification list on white") {
    SampleData.loadAll()
    return DockListView().padding(20).background(Color.white)
}

#Preview("Notification list, Reduce Transparency") {
    SampleData.loadAll()
    return DockListView().padding(20).background(previewWallpaper)
        .environment(\.forcedSurface, .solid)
}

#Preview("Dock on white") {
    SampleData.loadAll()
    AppState.shared.config.dockSpot = .bottomRight
    return DockView(model: DockModel())
        .frame(width: 600, height: 160)
        .background(Color.white)
}
