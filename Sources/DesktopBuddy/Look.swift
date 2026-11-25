import SwiftUI
import AppKit

// Shared look for the on-screen surfaces (dock, notification list, menu bar pop-up),
// chosen from the Mac's version and its accessibility settings.

/// What sits behind the dock logos and the notification list.
enum SurfaceKind: String, Equatable {
    case glass      // Liquid Glass (macOS 26 and later)
    case frosted    // dark frosted blur (macOS 14–15)
    case solid      // opaque: Reduce Transparency or Increase Contrast is on
}

enum Look {
    /// Reduce Transparency and Increase Contrast both ask for an opaque surface, so text never sits on
    /// whatever wallpaper or window happens to be behind it.
    static func surface(glassAvailable: Bool, reduceTransparency: Bool, increaseContrast: Bool) -> SurfaceKind {
        if reduceTransparency || increaseContrast { return .solid }
        return glassAvailable ? .glass : .frosted
    }

    static var glassAvailable: Bool {
        if #available(macOS 26, *) { return true }
        return false
    }

    /// Opaque fill used when the surface is solid: the dark grey of macOS dark mode, not pure black,
    /// so it still reads as a panel against a black desktop.
    static let solidFill = Color(red: 0.11, green: 0.11, blue: 0.12)

    /// White text on the dark surfaces. Measured against the lightest the glass gets (over a white window),
    /// these keep secondary text at 4.5:1 or better.
    struct OnDark {
        var increaseContrast: Bool
        var primary: Color { .white }
        /// Subject line, under the bold sender.
        var subhead: Color { .white.opacity(increaseContrast ? 1 : 0.92) }
        /// Preview text, service and time.
        var secondary: Color { .white.opacity(increaseContrast ? 0.9 : Look.secondaryOpacity) }
        /// Hairlines and resting outlines.
        var hairline: Color { .white.opacity(increaseContrast ? 0.5 : 0.12) }
    }
    static let secondaryOpacity = 0.74

    /// WCAG relative luminance of an sRGB colour (0 = black, 1 = white).
    static func luminance(r: Double, g: Double, b: Double) -> Double {
        func ch(_ v: Double) -> Double { v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
        return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)
    }
    /// White bold digits on a count, like macOS badges, unless the colour is too light for them
    /// (under 3:1, the bar for bold text): then black (yellow, orange, green, teal).
    static func prefersDarkText(r: Double, g: Double, b: Double) -> Bool {
        1.05 / (luminance(r: r, g: g, b: b) + 0.05) < 3
    }
    static func prefersDarkText(on color: Color) -> Bool {
        guard let c = NSColor(color).usingColorSpace(.sRGB) else { return false }
        return prefersDarkText(r: c.redComponent, g: c.greenComponent, b: c.blueComponent)
    }
}

/// Lets a preview show a surface the way an accessibility setting would draw it.
private struct ForcedSurfaceKey: EnvironmentKey { static let defaultValue: SurfaceKind? = nil }
extension EnvironmentValues {
    var forcedSurface: SurfaceKind? {
        get { self[ForcedSurfaceKey.self] }
        set { self[ForcedSurfaceKey.self] = newValue }
    }
}

/// Reads the Mac's settings (live: they update the moment someone changes them) and picks the surface.
private struct SurfaceReader<Content: View>: View {
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    @Environment(\.colorSchemeContrast) private var contrast
    @Environment(\.forcedSurface) private var forced
    let content: (SurfaceKind, Bool) -> Content
    init(@ViewBuilder content: @escaping (SurfaceKind, Bool) -> Content) { self.content = content }
    var body: some View {
        let increase = contrast == .increased
        let kind = forced ?? Look.surface(glassAvailable: Look.glassAvailable, reduceTransparency: reduceTransparency,
                                         increaseContrast: increase)
        content(kind, increase || forced == .solid)
    }
}

extension View {
    /// Behind each dock logo: Liquid Glass, the dark frosted pill on macOS 14–15, or solid.
    func dockPillSurface(open: Bool) -> some View {
        SurfaceReader { kind, strong in
            switch kind {
            case .glass:
                if #available(macOS 26, *) {
                    self.glassEffect(.regular.tint(.black.opacity(GlassTint.pill)).interactive(), in: Capsule())
                        .overlay(Capsule().strokeBorder(Color.white.opacity(open ? 0.7 : 0), lineWidth: 1))
                } else {
                    self
                }
            case .frosted:
                self.background(FrostedGlass().clipShape(Capsule()))
                    .overlay(Capsule().stroke(Color.white.opacity(open ? 0.6 : 0.22), lineWidth: open ? 1 : 0.5))
            case .solid:
                self.background(Capsule().fill(Look.solidFill))
                    .overlay(Capsule().strokeBorder(Color.white.opacity(open ? 0.9 : (strong ? 0.5 : 0.2)), lineWidth: open ? 1.5 : 1))
            }
        }
    }

    /// Behind the notification list: dark-tinted Liquid Glass, the black panel on macOS 14–15, or solid.
    func listPanelSurface(cornerRadius: CGFloat) -> some View {
        let shape = RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
        return SurfaceReader { kind, strong in
            switch kind {
            case .glass:
                if #available(macOS 26, *) {
                    self.glassEffect(.regular.tint(.black.opacity(GlassTint.panel)), in: shape)
                } else {
                    self
                }
            case .frosted:
                self.background(shape.fill(Color.black))
                    .overlay(shape.stroke(Color.white.opacity(0.12), lineWidth: 0.5))
            case .solid:
                self.background(shape.fill(Look.solidFill))
                    .overlay(shape.strokeBorder(Color.white.opacity(strong ? 0.5 : 0.16), lineWidth: 1))
            }
        }
    }
}

// MARK: - Spoken text

/// "just now", "4 minutes ago", "1 hour ago", "3 days ago": what VoiceOver says instead of "4m".
func spokenAgo(_ d: Date, now: Date = Date()) -> String {
    let s = max(0, Int(now.timeIntervalSince(d)))
    func unit(_ n: Int, _ word: String) -> String { "\(n) \(word)\(n == 1 ? "" : "s") ago" }
    if s < 60 { return "just now" }
    if s < 3600 { return unit(s / 60, "minute") }
    if s < 86_400 { return unit(s / 3600, "hour") }
    return unit(s / 86_400, "day")
}

extension BuddyItem {
    /// One sentence VoiceOver reads for a notification, in the order people scan it:
    /// what and who, then the message, then where it came from and when.
    func spokenSummary(service: String, owner: String?, now: Date = Date()) -> String {
        var parts: [String] = []
        let what = source == .reminder ? "Reminder" : service
        switch source {
        case .mail, .chat:
            parts.append("\(what) from \(headline)" + (subhead.map { ": \($0)" } ?? ""))
        default:
            parts.append("\(what): \(headline)" + (subhead.map { ", \($0)" } ?? ""))
        }
        if let d = detail, !d.isEmpty { parts.append(d) }
        var whereWhen = [String]()
        if let owner, source != .reminder { whereWhen.append(owner) }
        whereWhen.append(spokenAgo(date, now: now))
        parts.append(whereWhen.joined(separator: ", "))
        return parts.map { p in
            let s = p.prefix(1).uppercased() + p.dropFirst()
            return s.hasSuffix(".") || s.hasSuffix("?") || s.hasSuffix("!") ? s : s + "."
        }.joined(separator: " ")
    }
}
