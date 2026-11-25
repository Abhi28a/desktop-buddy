import Foundation

/// Made-up accounts and notifications for Xcode previews and `--snapshot`.
/// Only ever loaded into a throwaway data folder (see `Paths.dataFolder`), never real settings.
@MainActor
enum SampleData {
    /// Two signed-in sample accounts, if the folder has none yet.
    static func loadAccounts() {
        let state = AppState.shared
        if state.config.accounts.isEmpty {
            state.addAccount(AccountConfig(label: "Dynamatix", email: "you@dynamatix.co.uk"))
            state.addAccount(AccountConfig(label: "IE Hub", email: "you@iehub.org", tintChoice: .green))
        }
        state.signedIn = Set(state.config.accounts.map(\.id))
    }

    static func items(now: Date = Date()) -> [BuddyItem] {
        let accounts = AppState.shared.config.accounts
        let a1 = accounts.first?.id, a2 = accounts.dropFirst().first?.id ?? accounts.first?.id
        return [
            BuddyItem(id: "s1", source: .mail, accountID: a1, title: "Contract ready to sign", from: "Jane Cooper",
                      detail: "Hi, the final version is attached. Could you sign by Friday?", date: now, url: URL(string: "https://example.com")),
            BuddyItem(id: "s2", source: .chat, accountID: a2, title: "Project Apollo", from: "Priya Shah",
                      detail: "Can you review the new pricing page before the 3pm call?", date: now - 120, url: nil),
            BuddyItem(id: "s3", source: .meeting, accountID: a1, title: "Design review", from: "Priya Shah",
                      detail: "Starts in 5 min · Google Meet", date: now - 300, url: nil),
            BuddyItem(id: "s4", source: .reminder, accountID: nil, title: "💧 Drink a glass of water", detail: "Stay hydrated!", date: now - 600, url: nil),
            BuddyItem(id: "s5", source: .mail, accountID: a2, title: "Invoice #4821", from: "Accounts team",
                      detail: "Please find this month's invoice attached.", date: now - 900, url: nil),
            BuddyItem(id: "s6", source: .task, accountID: a1, title: "Send onboarding copy to Sam", from: "My tasks",
                      detail: "Due today", date: now - 1800, url: nil),
        ]
    }

    /// Sample accounts plus a full inbox, ready for a preview.
    static func loadAll() {
        loadAccounts()
        if AppState.shared.items.isEmpty { _ = AppState.shared.offer(items()) }
    }
}
