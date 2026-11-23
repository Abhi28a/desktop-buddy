using DesktopBuddy.Core;

namespace DesktopBuddy.Tests;

// Port of the rules scenarios in SelfTest.swift: client rules, muted mail and chats, account naming,
// sounds, settings from before an update, picked-chat identity, chips and short ages.
// Pure functions only (no storage), so these can run in parallel.

public class RulesTests
{
    private static BuddyItem Mail(string address, string subject, string preview = "", string from = "Jane Example") =>
        Fx.Mail("1", "a", address, subject, preview, from: from);

    private static BuddyItem Chat(string space, string reference, string accountID = "a", string text = "hi") =>
        Fx.Chat("1", accountID, space, reference, text);

    // MARK: Client rules

    private static ClientConfig AcmeClient() => new()
    {
        Name = "Acme",
        Senders = ["acme.example.com", "@partner.example.org", "Boss@Client.example.com"],
        Keywords = ["FEMA", "  invoice "],
        MatchChatNames = true,
    };

    [Fact]
    public void SenderRules()
    {
        var c = AcmeClient();
        bool M(BuddyItem i) => ClientMatcher.Matches(c, i);
        Assert.True(M(Mail("jane@acme.example.com", "Hi")));                       // sender domain matches
        Assert.True(M(Mail("x@mail.acme.example.com", "Hi")));                     // sender subdomain matches
        Assert.False(M(Mail("x@notacme.example.com", "Hi")));                      // look-alike domain does not match
        Assert.True(M(Mail("ops@partner.example.org", "Hi")));                     // rule typed with leading @ works
        Assert.True(M(Mail("boss@client.example.com", "Hi")));                     // exact address, any case
        Assert.False(M(Mail("intern@client.example.com", "Hi")));                  // other address at that domain is not the exact address
    }

    [Fact]
    public void KeywordRules()
    {
        var c = AcmeClient();
        bool M(BuddyItem i) => ClientMatcher.Matches(c, i);
        Assert.True(M(Mail("a@b.example.com", "fema rollout")));                   // keyword in subject, any case
        Assert.True(M(Mail("a@b.example.com", "Hello", "Attached the invoice.")));  // keyword in preview
        Assert.True(M(Mail("a@b.example.com", "INVOICE #4")));                     // keyword with spaces trimmed
        Assert.False(M(Mail("a@b.example.com", "female founders")));               // whole word (FEMA is not FEMALE)
        Assert.False(M(Mail("a@b.example.com", "Lunch?")));                        // unrelated mail not matched
        Assert.True(M(Mail("a@b.example.com", "Hello", from: "FEMA Desk")));       // sender name counts too
    }

    [Fact]
    public void ChatRules()
    {
        var c = AcmeClient();
        bool M(BuddyItem i) => ClientMatcher.Matches(c, i);
        Assert.True(M(Chat("Acme Team", "spaces/X")));                             // chat named after client matches
        Assert.False(M(Chat("Acmes R Us", "spaces/Y")));                           // chat name is whole-word
        c.MatchChatNames = false;
        Assert.False(M(Chat("Acme Team", "spaces/X")));                            // chat-name matching can be turned off
        c.Chats = [new ChatRef { AccountID = "a", ChatID = "spaces/PICK", Name = "Ops" }];
        Assert.True(M(Chat("Ops", "spaces/PICK")));                                // picked chat matches on its account
        Assert.False(M(Chat("Ops", "spaces/PICK", accountID: "b")));               // same chat ID on another account does not
        var meeting = new BuddyItem { Id = "meet:a:1", Source = ItemSource.meeting, AccountID = "a", Title = "Acme sync", Date = DateTimeOffset.Now };
        Assert.False(M(meeting));                                                  // meetings are never re-assigned
    }

    [Fact]
    public void ContainsWordIsWholeWordAndCaseInsensitive()
    {
        Assert.False(ClientMatcher.ContainsWord("   ", "anything"));               // empty keyword is ignored
        Assert.False(ClientMatcher.ContainsWord("Hub", "Pushed to GitHub"));       // short keyword is whole-word
        Assert.True(ClientMatcher.ContainsWord("Hub", "IE Hub weekly"));
        Assert.True(ClientMatcher.ContainsWord("R&D", "Q4 R&D budget"));           // keyword with punctuation
        Assert.True(ClientMatcher.ContainsWord("hub", "THE HUB."));
        Assert.True(ClientMatcher.ContainsWord("café", "Meet at the Café today"));  // letters beyond ASCII count as letters
        Assert.False(ClientMatcher.ContainsWord("cafe", "Meet at the Café today"));
        Assert.False(ClientMatcher.ContainsWord("rep", "report"));
        Assert.True(ClientMatcher.ContainsWord("a.b", "see a.b now"));             // a dot is a dot, not "any character"
        Assert.False(ClientMatcher.ContainsWord("a.b", "see axb now"));
    }

    [Fact]
    public void DirectMessagesMatchThePersonYouTalkTo()
    {
        var dmItem = new BuddyItem
        {
            Id = "chat:x:1", Source = ItemSource.chat, AccountID = "acc-m", Title = "Direct message",
            From = "Sam Taylor", Date = DateTimeOffset.Now, ChatRef = "spaces/OTHER",
        };
        var byName = new ClientConfig { Name = "Sam", MatchChatNames = true };
        Assert.True(ClientMatcher.Matches(byName, dmItem));                        // name matching also covers a DM with that person
        Assert.False(ClientMatcher.Matches(new ClientConfig { Name = "Sam", MatchChatNames = false }, dmItem));
        Assert.False(ClientMatcher.Matches(byName, dmItem with { Title = "Project room" }));
    }

    [Fact]
    public void SenderMatchesIgnoresEmptyRules()
    {
        var item = Mail("a@b.example.com", "x");
        Assert.False(ClientMatcher.SenderMatches("", item));
        Assert.False(ClientMatcher.SenderMatches(" @ ", item));
        Assert.False(ClientMatcher.SenderMatches("example.com", item with { FromAddress = null }));
    }

    [Fact]
    public void AssignTagsTheFirstEnabledMatchingClientAndRecomputesEveryTime()
    {
        var first = new ClientConfig { Name = "First", Senders = ["acme.example.com"] };
        var second = new ClientConfig { Name = "Second", Senders = ["acme.example.com"] };
        var offAcme = new ClientConfig { Name = "Off", Enabled = false, Senders = ["acme.example.com"] };
        var otherAccount = new ClientConfig { Name = "Other", AccountIDs = ["b"], Senders = ["acme.example.com"] };
        var item = Mail("jane@acme.example.com", "Hi") with { ClientID = "stale" };
        Assert.Equal(first.Id, ClientMatcher.Assign([item], "a", [offAcme, otherAccount, first, second])[0].ClientID);
        Assert.Null(ClientMatcher.Assign([item], "a", [offAcme, otherAccount])[0].ClientID);    // stale tags are cleared
        Assert.Equal(otherAccount.Id, ClientMatcher.Assign([item], "b", [otherAccount])[0].ClientID);
        Assert.Equal("stale", item.ClientID);                                                    // the input is not modified
    }

    // MARK: Muted mail and chats

    private static AccountConfig MuteAccount() => new()
    {
        Id = "acc-g", Label = "Northwind", Email = "me@northwind.example.com",
        MutedSenders = ["news.example.com", "noreply@shop.example.com"], MutedKeywords = ["newsletter"],
    };

    [Fact]
    public void MutedMail()
    {
        var g = MuteAccount();
        bool Muted(BuddyItem i) => MailFilter.IsMuted(i, g);
        Assert.True(Muted(Mail("a@news.example.com", "Hi")));                      // muted domain
        Assert.True(Muted(Mail("a@eu.news.example.com", "Hi")));                   // muted domain's subdomain
        Assert.False(Muted(Mail("a@fakenews.example.com", "Hi")));                 // look-alike domain is not muted
        Assert.True(Muted(Mail("noreply@shop.example.com", "Order")));             // muted exact address
        Assert.False(Muted(Mail("help@shop.example.com", "Order")));               // other address at that domain is not
        Assert.True(Muted(Mail("a@b.example.com", "Our NEWSLETTER")));             // muted keyword, any case
        Assert.False(Muted(Mail("a@b.example.com", "newsletters archive")));       // keyword is a whole word
        Assert.Empty(new AccountConfig { Label = "x", Email = "" }.MutedSenders);  // old settings have no mute rules
    }

    [Fact]
    public void MuteApplyDropsMutedItemsAndTheirLiveIds()
    {
        var g = MuteAccount();
        var keep = Fx.Mail("f1", "acc-g", "jo@client.example.com", "Contract");
        var drop = Fx.Mail("f2", "acc-g", "a@news.example.com", "Weekly");
        var r = new FetchResult { Source = ItemSource.mail, Items = [keep, drop], LiveIDs = [keep.Id, drop.Id] };
        var dropped = MailFilter.Apply(r, g);
        Assert.Equal([keep.Id], r.Items.Select(i => i.Id));                        // fetch keeps wanted mail
        Assert.True(!r.LiveIDs.Contains(drop.Id) && dropped.SetEquals([drop.Id])); // muted mail leaves the live list too
    }

    [Fact]
    public void MuteApplyLeavesMeetingsAndTasksAlone()
    {
        var g = MuteAccount();
        g.MutedKeywords = ["sync"];
        var meeting = new BuddyItem { Id = "meet:acc-g:1", Source = ItemSource.meeting, AccountID = "acc-g", Title = "Daily sync", Date = DateTimeOffset.Now };
        var r = new FetchResult { Source = ItemSource.meeting, Items = [meeting], LiveIDs = [meeting.Id] };
        Assert.Empty(MailFilter.Apply(r, g));
        Assert.Single(r.Items);
        Assert.False(MailFilter.IsMuted(meeting, g));
    }

    [Fact]
    public void MutedChats()
    {
        var h = MuteAccount();
        h.MutedChats = [new ChatRef { AccountID = h.Id, ChatID = "spaces/LOUD", Name = "All hands" }];
        h.MutedKeywords = ["standup"];
        var plainMail = new AccountConfig { MutedSenders = ["x.example.com"] };
        Assert.False(MailFilter.IsMuted(Fx.Chat("c1", h.Id, "Hello", "s"), plainMail));            // mail senders don't mute chats
        Assert.True(MailFilter.IsMuted(Fx.Chat("c2", h.Id, "All hands", "spaces/LOUD"), h));       // a muted chat is muted
        Assert.False(MailFilter.IsMuted(Fx.Chat("c3", h.Id, "Design", "spaces/QUIET"), h));        // other chats are not
        Assert.True(MailFilter.IsMuted(Fx.Chat("c4", h.Id, "Design", "x", "Standup in 5"), h));    // chat keyword in the message
        Assert.True(MailFilter.IsMuted(Fx.Chat("c5", h.Id, "Daily standup", "y"), h));             // chat keyword in the chat name
        Assert.False(MailFilter.IsMuted(Fx.Chat("c6", h.Id, "Design", "z", "standups"), h));       // chat keyword is a whole word
        Assert.True(MailFilter.IsMuted(Fx.Mail("c7", h.Id, "a@b.example.com", "standup"), h));     // one keyword list mutes mail too
        Assert.Empty(new AccountConfig { Label = "x", Email = "" }.MutedChats);                    // old settings have no chat mutes
    }

    [Fact]
    public void ChatFetchDropsMutedChats()
    {
        var h = MuteAccount();
        h.MutedChats = [new ChatRef { AccountID = h.Id, ChatID = "spaces/LOUD", Name = "All hands" }];
        var loud = Fx.Chat("c8", h.Id, "All hands", "spaces/LOUD");
        var calm = Fx.Chat("c9", h.Id, "Design", "spaces/QUIET");
        var cr = new FetchResult { Source = ItemSource.chat, Items = [loud, calm], LiveIDs = [loud.Id, calm.Id] };
        MailFilter.Apply(cr, h);
        Assert.True(cr.Items.Select(i => i.Id).SequenceEqual([calm.Id]) && !cr.LiveIDs.Contains(loud.Id));   // chat fetch drops muted chats
    }

    // MARK: Account naming after sign-in

    [Theory]
    [InlineData("jo@dynamatix.co.uk", "Dynamatix")]
    [InlineData("jo@iehub.org", "IE Hub")]
    [InlineData("jo@acme-ltd.co.uk", "Acme Ltd")]
    [InlineData("jo@mail.example.com", "Mail")]                // the first real part
    [InlineData("jo@contoso.onmicrosoft.com", "Contoso")]
    [InlineData("jo@gmail.com", "Personal")]
    [InlineData("Jo@Dynamatix.CO.UK", "Dynamatix")]            // mixed case
    [InlineData("jo@outlook.com", "Personal")]
    [InlineData("jo@some_company.net", "Some Company")]
    [InlineData("", "Account")]
    [InlineData("jo@", "Jo")]
    [InlineData("jo@co.uk", "Co")]                             // only generic parts: the first one
    public void SuggestedLabel(string email, string expected) => Assert.Equal(expected, AccountNaming.SuggestedLabel(email));

    [Fact]
    public void UniqueLabelAddsANumberWhenTaken()
    {
        Assert.Equal("Dynamatix 2", AccountNaming.UniqueLabel("Dynamatix", new HashSet<string> { "Dynamatix" }));                    // taken name gets a number
        Assert.Equal("Dynamatix 3", AccountNaming.UniqueLabel("Dynamatix", new HashSet<string> { "Dynamatix", "Dynamatix 2" }));    // next free number
        Assert.Equal("IE Hub", AccountNaming.UniqueLabel("IE Hub", new HashSet<string> { "Dynamatix" }));                           // free name unchanged
    }

    [Fact]
    public void GenericKeywordListMatchesTheMac()
    {
        string[] expected = ["team", "the", "and", "update", "meeting", "hi", "hello", "re", "fw", "fwd",
                             "please", "thanks", "urgent", "info", "request", "report", "project", "test"];
        Assert.True(AccountNaming.GenericKeywords.SetEquals(expected));
    }

    // MARK: Logos

    [Fact]
    public void BundledLogosAreGuessedFromNameOrEmailDomain()
    {
        Assert.Equal(["dynamatix", "iehub", "riskhawk", "gatehouse"], Logos.BundledKeys);
        Assert.Equal("dynamatix", Logos.GuessKey("Dynamatix"));
        Assert.Equal("iehub", Logos.GuessKey("IE Hub"));                           // spaces are ignored
        Assert.Equal("iehub", Logos.GuessKey("Work", "jo@iehub.org"));              // or the email
        Assert.Equal("riskhawk", Logos.GuessKey("RiskHawk Ltd"));
        Assert.Null(Logos.GuessKey("Acme", "jo@acme.example.com"));
        Assert.Equal("dynamatix", Logos.GuessKey("x", "dynamatix.com"));
    }

    [Fact]
    public void ResolveUsesAPickedFileThenABundledLogoThenNothing()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(new LogoRef(file, null), Logos.Resolve(file, "Dynamatix"));          // a picked file wins
            Assert.Equal(new LogoRef(null, "dynamatix"), Logos.Resolve(file + ".gone", "Dynamatix"));   // missing file: bundled by name
            Assert.Null(Logos.Resolve(null, "Acme"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void MonogramIsTheFirstLettersOfTwoWords()
    {
        Assert.Equal("AL", Logos.Monogram("acme ltd"));
        Assert.Equal("N", Logos.Monogram("Northwind"));
        Assert.Equal("AB", Logos.Monogram("A  B  C"));
        Assert.Equal("", Logos.Monogram(""));
    }

    // MARK: Sounds

    [Fact]
    public void SoundsPlayTheMostUrgentTypeInABatch()
    {
        BuddyItem[] items =
        [
            Fx.Mail("x", "a", "a@b.example.com", "s"),
            new() { Id = "meet:a:1", Source = ItemSource.meeting, AccountID = "a", Title = "Sync", Date = DateTimeOffset.Now },
        ];
        Assert.Equal(ItemSource.meeting, SoundRules.LeadSource(items));            // meeting sound wins over mail in one batch
        Assert.Null(SoundRules.LeadSource([]));                                    // nothing new, no sound
        Assert.Equal(ItemSource.chat, SoundRules.LeadSource([items[0], Fx.Chat("c", "a", "Team", "s")]));
    }

    [Fact]
    public void EveryTypeDefaultsToTheDefaultSoundAndCanBeChanged()
    {
        var c = new AppConfig();
        Assert.All(ItemSourceInfo.All, s => Assert.Equal(AppConfig.DefaultSound, c.Sound(s)));
        c.SetSound("Hero", ItemSource.meeting);
        c.SetSound(AppConfig.NoSound, ItemSource.mail);
        Assert.True(c.Sound(ItemSource.meeting) == "Hero" && c.Sound(ItemSource.chat) == AppConfig.DefaultSound);   // a type can have its own sound
        Assert.Equal(AppConfig.NoSound, c.Sound(ItemSource.mail));                 // a type can be silent
    }

    [Fact]
    public void SettingsFromBeforeThisUpdateStillLoad()
    {
        const string old = """{"accounts":[{"label":"A","email":"a@b.example.com","id":"1","enabled":true,"watchMail":true,"watchChat":true,"watchMeetings":true,"watchTasks":true,"clientID":"","redirectPort":8766}],"reminders":[],"pollSeconds":60,"meetingLeadMinutes":10,"playSound":true,"paused":false}""";
        var cfg = JsonFile.Decode<AppConfig>(old)!;
        Assert.False(cfg.Accounts[0].LabelsOnly);
        Assert.Equal(AppConfig.DefaultSound, cfg.Sound(ItemSource.chat));
        Assert.True(cfg.CheckForUpdates);
    }

    // MARK: Parsing what the chips field accepts

    [Fact]
    public void ChipsAreCommaSeparatedTrimmedAndDeduped()
    {
        Assert.Equal(["b.example.com", "a.example.com"], TokenRules.Merge(" a.example.com, B.example.com ,, a.EXAMPLE.com ", ["b.example.com"]));
    }

    [Fact]
    public void SenderAndKeywordProblems()
    {
        Assert.NotNull(TokenRules.SenderProblem("acme"));                          // no domain ending is rejected
        Assert.NotNull(TokenRules.SenderProblem("acme house.com"));                // a space is rejected
        Assert.NotNull(TokenRules.SenderProblem("jane@"));                         // half an address is rejected
        Assert.True(TokenRules.SenderProblem("acme.co.uk") is null && TokenRules.SenderProblem("jane@acme.co.uk") is null
                    && TokenRules.SenderProblem("@partner.io") is null);           // domain and full address are accepted
        Assert.NotNull(TokenRules.KeywordProblem("ab"));                           // keywords under 3 letters are rejected
        Assert.Null(TokenRules.KeywordProblem("FEMA"));
        Assert.Equal("“acme” isn't a domain. Add the ending, e.g. acme.co.uk or acme.com.", TokenRules.SenderProblem("acme"));
        Assert.Equal("“jane@x” isn't a complete email address.", TokenRules.SenderProblem("jane@x"));
        Assert.Equal("“jane@” isn't a domain. Add the ending, e.g. jane.co.uk or jane.com.", TokenRules.SenderProblem("jane@"));   // the trailing @ is trimmed first, as on the Mac
    }

    [Fact]
    public void ShortAgesReadLikeNotificationCenter()
    {
        var now = Fx.At(3, 12);
        Assert.Equal("now", TokenRules.ShortAgo(now, now));
        Assert.Equal("5m", TokenRules.ShortAgo(now.AddSeconds(-300), now));
        Assert.Equal("2h", TokenRules.ShortAgo(now.AddSeconds(-7200), now));
        Assert.Equal("3d", TokenRules.ShortAgo(now.AddDays(-3), now));
        Assert.Equal("now", TokenRules.ShortAgo(now.AddMinutes(5), now));          // never negative
    }

    [Fact]
    public void APickedChatStaysPickedAfterItsNameIsResolved()
    {
        var saved = new ChatRef { AccountID = "a", ChatID = "spaces/DM1", Name = "Direct message" };
        var relisted = new ChatRef { AccountID = "a", ChatID = "spaces/DM1", Name = "Priya Example", Kind = ChatKind.direct, LastActive = DateTimeOffset.Now };
        Assert.Contains(relisted, new HashSet<ChatRef> { saved });
        Assert.NotEqual(saved, new ChatRef { AccountID = "b", ChatID = "spaces/DM1", Name = "Priya Example" });   // same space on another account is a different chat
    }

    // MARK: Copies

    [Fact]
    public void ClonesDoNotShareLists()
    {
        var a = MuteAccount();
        a.MutedChats = [new ChatRef { AccountID = "x", ChatID = "c", Name = "n" }];
        a.OnlyLabels = [new MailLabel { Id = "L1", Name = "Urgent" }];
        var copy = a.Clone();
        copy.MutedSenders.Add("other.example.com");
        copy.MutedChats[0].Name = "changed";
        copy.OnlyLabels.Clear();
        Assert.Equal(2, a.MutedSenders.Count);
        Assert.Equal("n", a.MutedChats[0].Name);
        Assert.Single(a.OnlyLabels);
        var c = new ClientConfig { Name = "A", Keywords = ["one"], Chats = [new ChatRef { ChatID = "z" }] };
        var cc = c.Clone();
        cc.Keywords.Add("two");
        cc.Chats.Clear();
        Assert.Single(c.Keywords);
        Assert.Single(c.Chats);
    }

    [Fact]
    public void NullAndEmptyOptionalListsDiffer()
    {
        Assert.True(CopyExtensions.SameList<string>(null, null));
        Assert.False(CopyExtensions.SameList<string>(null, []));
        Assert.True(CopyExtensions.SameList(["a"], ["a"]));
        Assert.False(CopyExtensions.SameList(["a"], ["b"]));
    }
}
