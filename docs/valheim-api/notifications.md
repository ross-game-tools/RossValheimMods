# Top-left notifications: `MessageHud`, pickups, and skill XP

What this covers: how vanilla shows item-pickup and skill-level-up
messages in the top-left, exactly how they fade, whether there is a
queue or a single slot, where a pickup message and a skill-up message
originate, what the game knows at the moment skill XP is gained, and
the safest hook points for a mod that wants a stacking, update-in-place
notification list plus a raw-XP-gain display that vanilla does not have.

Produced on 2026-09-19 by decompiling
`valheim_Data/Managed/assembly_valheim.dll` with ilspycmd 8.2
(`8.2.0.7535-95108c96`). Game version not re-confirmed this session;
`death-and-respawn.md` read the same install at 1.0.14 on 2026-09-18 and
nothing here depends on a version-sensitive signature. Everything quoted
below is real decompiled code; line numbers are from `ilspycmd -t
<Type>` per-type output and drift between versions.

## Recommendation

**Suppress vanilla's top-left path and draw our own stacking list**
(option b), not "take over `MessageHud`'s area" (option a) — because
there is no area to take over. `MessageHud` does not pool per-message
UI elements for `TopLeft`; it has exactly **one** `TMP_Text`
(`m_messageText`) and **one** `Image` (`m_messageIcon`), fed by a
`Queue<MsgData>` that is drained one entry at a time, at least 1 second
apart. There is nothing to clone-and-multiply the way `ClockDisplay`
clones `Minimap.m_biomeNameSmall` — the "pool" here is a queue of depth
1 displayed serially, not parallel slots.

Vanilla's own stacking trick (`MsgData.m_amount` folding onto the
currently-shown message, "Wood x12" -> "x20") only merges the **next**
queued message into whatever is **currently displayed**, checked by
`text == currentMsg.m_text && icon == currentMsg.m_icon` and a `< 4f`
timer — a positional check against one slot, not an identity-keyed
lookup across a list. If a skill-up message queues between two Wood
pickups, the second Wood message no longer matches `currentMsg` and
starts a new entry instead of merging. This is exactly the overwriting
behaviour the author wants fixed, and it cannot be fixed by feeding more
messages into `MessageHud`'s existing plumbing — the single-slot queue
has to be bypassed.

So: suppress vanilla's top-left path entirely (return `false` from a
`Prefix` on `MessageHud.ShowMessage` for `MessageType.TopLeft`, see
below) and maintain our own `List<Entry>` of cloned `m_messageText` +
`m_messageIcon` pairs, keyed by item name (pickup) or `SkillType`
(skill-up/XP), each with the same 1s-hold + `CrossFadeAlpha(0, 4f)` fade
vanilla uses. `MessageType.Center` is untouched either way — it is a
completely separate code path (a `_crossFadeTextBuffer` entry on
`m_messageCenterText`), not something a TopLeft change can affect.

## 1. The message path

`MessageHud.MessageType` (MessageHud.cs) is a two-value enum:
`TopLeft = 1, Center = 2`. Every message reaches `MessageHud` through
one public entry point regardless of source:

```csharp
public void ShowMessage(MessageType type, string text, int amount = 0, Sprite icon = null,
    bool showDespiteHiddenHUD = false, bool log = true)
{
    m_showDespiteHiddenHUD = showDespiteHiddenHUD;
    if (Hud.IsUserHidden() && !showDespiteHiddenHUD) return;
    text = Localization.instance.Localize(text);
    switch (type)
    {
    case MessageType.TopLeft:
    {
        MsgData msgData = new MsgData { m_icon = icon, m_text = text, m_amount = amount };
        m_msgQeue.Enqueue(msgData);
        if (log) AddLog(text);
        break;
    }
    case MessageType.Center:
        m_messageCenterText.text = text;
        _crossFadeTextBuffer.Add(new CrossFadeText { text = m_messageCenterText, alpha = 1f, time = 0f });
        _crossFadeTextBuffer.Add(new CrossFadeText { text = m_messageCenterText, alpha = 0f, time = 4f });
        if (log) AddLog(text);
        break;
    }
}
```

Both the local-owner path and the network path funnel here:
`Character.Message` is `public virtual void Message(...) { }` (no-op
base, Character.cs:3784); `Player.Message` overrides it
(Player.cs:5388):

```csharp
public override void Message(MessageHud.MessageType type, string msg, int amount = 0, Sprite icon = null, bool log = false)
{
    if (m_nview == null || !m_nview.IsValid()) return;
    if (m_nview.IsOwner())
    {
        if ((bool)MessageHud.instance)
            MessageHud.instance.ShowMessage(type, msg, amount, icon, showDespiteHiddenHUD: false, log);
    }
    else m_nview.InvokeRPC("Message", (int)type, msg, amount);
}

private void RPC_Message(long sender, int type, string msg, int amount)
{
    if (m_nview.IsOwner() && (bool)MessageHud.instance)
        MessageHud.instance.ShowMessage((MessageHud.MessageType)type, msg, amount);
}
```

So a remote-triggered message (e.g. another system messaging this
player) arrives via RPC and still lands in `MessageHud.ShowMessage` on
the owner's client. There is also `MessageHud.MessageAll(type, text)` →
`ZRoutedRpc.InvokeRoutedRPC(0L, "ShowMessage", ...)` → the same
`RPC_ShowMessage` → `ShowMessage`. **`MessageHud.ShowMessage` is the one
true choke point for every top-left message, from every source.**

**What happens when a second message arrives while one is showing**:
it does not overwrite — it queues (`m_msgQeue.Enqueue`), FIFO, and
`UpdateMessage` drains one entry per >=1 real second (see §2). What
*looks* like overwriting to a player is the 1-entry display slot
showing them in rapid succession, each replacing the visible text the
instant it's dequeued, with no visual stacking. There is no cap or
drop-when-full on `m_msgQeue` — a burst of pickups queues up and drains
serially, so a busy inventory session can visibly lag the display by
several seconds behind the actual pickups.

## 2. The fade — code constants, not asset data

`UpdateMessage(float dt)` (MessageHud.cs), run every `Update()`:

```csharp
private void UpdateMessage(float dt)
{
    if ((double)dt > 0.5) return;
    if (_crossFadeTextBuffer.Count > 0)
    {
        CrossFadeText crossFadeText = _crossFadeTextBuffer[0];
        _crossFadeTextBuffer.RemoveAt(0);
        crossFadeText.text.CrossFadeAlpha(crossFadeText.alpha, crossFadeText.time, ignoreTimeScale: true);
    }
    m_msgQueueTimer += dt;
    if (m_msgQeue.Count <= 0) return;
    MsgData msgData = m_msgQeue.Peek();
    bool flag = m_msgQueueTimer < 4f && msgData.m_text == currentMsg.m_text && msgData.m_icon == currentMsg.m_icon;
    if (m_msgQueueTimer >= 1f || flag)
    {
        MsgData msgData2 = m_msgQeue.Dequeue();
        m_messageText.text = msgData2.m_text;
        if (flag) msgData2.m_amount += currentMsg.m_amount;
        if (msgData2.m_amount > 1) m_messageText.text = m_messageText.text + " x" + msgData2.m_amount;
        _crossFadeTextBuffer.Add(new CrossFadeText { text = m_messageText, alpha = 1f, time = 0f });
        _crossFadeTextBuffer.Add(new CrossFadeText { text = m_messageText, alpha = 0f, time = 4f });
        if (msgData2.m_icon != null)
        {
            m_messageIcon.sprite = msgData2.m_icon;
            m_messageIcon.canvasRenderer.SetAlpha(1f);
            m_messageIcon.CrossFadeAlpha(0f, 4f, ignoreTimeScale: true);
        }
        else m_messageIcon.canvasRenderer.SetAlpha(0f);
        currentMsg = msgData2;
        m_msgQueueTimer = 0f;
    }
}
```

All four numbers here — the `1f` second minimum dwell before the next
queued entry can display, the `4f` second merge window, and the `4f`
second `CrossFadeAlpha` fade-out (applied identically to both text and
icon) — are **plain C# literals in code**, not serialized fields. They
are not asset data; they can be read and reproduced exactly, and there
is no runtime-only value to chase here. The fade itself is a single
instantaneous jump to `alpha=1` at `time=0`, then Unity's
`CrossFadeAlpha(0f, 4f, ...)` linearly (`ignoreTimeScale: true`, so it
keeps fading through pause menus) interpolates alpha to 0 over the next
4 real seconds — no separate hold phase; the fade starts immediately
and *is* the display duration.

## 3. The UI objects

Serialized fields on `MessageHud` (public, so reachable at runtime off
`MessageHud.instance`):

```csharp
public TMP_Text m_messageText;      // the single top-left text — reused for every TopLeft message
public Image m_messageIcon;         // the single top-left icon — reused likewise
public TMP_Text m_messageCenterText;
public GameObject m_unlockMsgPrefab;      // separate system: "new recipe/build unlocked" cards, own pooled slots
public GameObject m_biomeFoundPrefab;     // separate system: full-screen biome banner
```

`m_messageText`/`m_messageIcon` are **one text element and one icon**,
not a pooled set — confirmed by the private `MsgData currentMsg` field
(singular) and the absence of any per-message `GameObject` instantiation
in `UpdateMessage`, unlike `UpdateUnlockMsg` (below) which does
instantiate per entry. Both are reachable at runtime
(`MessageHud.instance.m_messageText`), so a stacking-list feature can
clone `m_messageText`'s `GameObject` (for its `TMP_Text` styling,
`RectTransform`, and any `Animator`/material it carries) the same way
`ClockDisplay` clones `Minimap.m_biomeNameSmall` — see
`RossQoL/src/RossQoL.Game/Interface/ClockDisplay.cs`. `m_messageIcon`
similarly clones for the per-entry icon `Image`.

By contrast, the game's own *actually pooled* top-left-adjacent system
is `m_unlockMsgPrefab`/`m_unlockMessages` (a `List<GameObject>` sized to
`m_maxUnlockMessages`, default-initialized to `4` slots of `null`),
instantiated per entry in `UpdateUnlockMsg` and stacked vertically by
`anchoredPosition.y -= m_maxUnlockMsgSpace * slotIndex`. That is the
prefab-wiring precedent worth copying structurally (multiple live
instances, each destroyed independently when its `Animator` reaches an
animation state tagged `"done"`) — but it is a different feature
("$topic: $description" unlock cards), not reusable as our container.

`m_maxUnlockMsgSpace = 110`, `m_maxUnlockMessages = 4`,
`m_maxLogMessages = 50` are public serialized `int` fields — code
defaults shown; the shipped `MessageHud` prefab may override them
(read at runtime per the README's asset-data caveat).

## 4. Who raises a pickup message

`Character.ShowPickupMessage(ItemDrop.ItemData item, int amount)`
(Character.cs:3774 — note: on `Character`, **not** `Humanoid` or
`Player`; the call site in `Humanoid.Pickup` resolves it up the
inheritance chain, which is why `ilspycmd -t Humanoid` shows the call
but not the body):

```csharp
public void ShowPickupMessage(ItemDrop.ItemData item, int amount)
{
    Message(MessageHud.MessageType.TopLeft, "$msg_added " + item.m_shared.m_name, amount, item.GetIcon());
}
```

Called from `Humanoid.Pickup(GameObject go, bool autoequip = true, bool autoPickupDelay = true)`
(Humanoid.cs:608), only for `IsPlayer()`:

```csharp
int stack = component.m_itemData.m_stack;          // captured BEFORE AddItem — this pickup's own stack size
bool flag = m_inventory.AddItem(component.m_itemData);
...
ZNetScene.instance.Destroy(go);
if (autoequip && ...) EquipItem(component.m_itemData);
m_pickupEffects.Create(base.transform.position, Quaternion.identity, null, 1f, -1, GetZDOID());
if (IsPlayer())
{
    ShowPickupMessage(component.m_itemData, stack);
    if (Player.m_localPlayer == this as Player && Hud.instance.m_radialMenu.Active)
        Hud.instance.m_radialMenu.OnAddItem(component.m_itemData);
}
```

So `amount` is **this pickup's stack size** (e.g. picking up a 5-Wood
drop reports `amount: 5`), not the resulting inventory total — vanilla's
own "x12 -> x20" stacking in `MessageHud` is what turns a sequence of
per-pickup amounts into a running total on screen, not anything
`ShowPickupMessage` computes. Both the item identity (`item.m_shared`,
carrying `m_name`) and the icon (`item.GetIcon()`) are fully resolved
and available at this call — nothing further needs to be looked up.
`ItemDrop.ItemData` is a type **nested inside `ItemDrop`**; per
`death-and-respawn.md` §9, any `CompatMember` naming it must use
`"ItemDrop+ItemData"`, not the dotted form.

## 5. Skill XP

`Skills.Skill.Raise(float factor)` (Skills.cs, nested `Skill` class):

```csharp
public bool Raise(float factor)
{
    if (m_level >= 100f) return false;
    float num = m_info.m_increseStep * factor * Game.m_skillGainRate;
    m_accumulator += num;
    float nextLevelRequirement = GetNextLevelRequirement();
    if (m_accumulator >= nextLevelRequirement)
    {
        m_level += 1f;
        m_level = Mathf.Clamp(m_level, 0f, 100f);
        m_accumulator = 0f;
        return true;   // level crossed
    }
    return false;       // XP gained, no level crossed
}
private float GetNextLevelRequirement() => Mathf.Pow(Mathf.Floor(m_level + 1f), 1.5f) * 0.5f + 0.5f;
```

`Skill.m_info` (a `SkillDef`), `m_level`, and `m_accumulator` are all
**public fields**, and `SkillDef.m_increseStep` (sic, misspelled in the
game's own source) is a public serialized field with a code default of
`1f` — the actual per-skill values are asset data, unverified here
(README §"What is not in the DLLs"); read them at runtime off
`SkillDef.m_increseStep` if an exact number is needed. **These raw
`m_accumulator` numbers are not meaningful to a player as-is** — they
are internal progress units toward `GetNextLevelRequirement()`, whose
target itself grows with level (`floor(level+1)^1.5 * 0.5 + 0.5`), so
the same raw delta represents a shrinking fraction of a level as the
skill rises. Show XP gain as a **percentage of the next-level
requirement** (`delta / GetNextLevelRequirement()`), not the raw
`m_increseStep`-derived number, if it's meant to read as progress.

`Skills.RaiseSkill(SkillType skillType, float factor = 1f)` (Skills.cs,
public, calls the above):

```csharp
public void RaiseSkill(SkillType skillType, float factor = 1f)
{
    if (skillType == SkillType.None) return;
    Skill skill = GetSkill(skillType);          // private — no public single-skill accessor
    float level = skill.m_level;
    if (skill.Raise(factor))
    {
        if (m_useSkillCap) RebalanceSkills(skillType);
        m_player.OnSkillLevelup(skillType, skill.m_level);
        MessageHud.MessageType type = ((int)level != 0) ? MessageHud.MessageType.TopLeft : MessageHud.MessageType.Center;
        m_player.Message(type, "$msg_skillup $skill_" + skill.m_info.m_skill.ToString().ToLower() + ": " + (int)skill.m_level, 0, skill.m_info.m_icon);
        Gogan.LogEvent("Game", "Levelup", skillType.ToString(), (int)skill.m_level);
    }
}
```

What the game knows at the moment XP is gained, and what it currently
reports: `RaiseSkill` only acts — and only messages — **when a level
is crossed** (`skill.Raise(factor)` returns `true`); every other XP tick
(the vast majority of calls — most swings/actions raise a skill by a
small fraction well under one level) is silent, both in the UI and in
any callback. There is no vanilla event, message, or return value for
"XP was gained but no level crossed" — this is exactly the gap the
author wants filled. `GetSkill(SkillType)` (Skills.cs:345) is
**private**; there is no public single-skill lookup, but
`Skills.GetSkillList()` (confirmed public, `List<Skill>`,
`death-and-respawn.md` doesn't cover this but the signature was seen
alongside `GetSkillFactor`/`GetSkillLevel` in this session's dump) lets
a `Postfix` find the same `Skill` instance to read its
now-`public m_level`/`m_accumulator` after the call.

Skill level-up **is** already routed through the same `MessageHud`
TopLeft pipeline as pickups (§1), with the skill icon
(`skill.m_info.m_icon`) attached — it will already interleave/overwrite
against pickup messages in vanilla's single slot, which is the concrete
case the author described.

`Player.OnSkillLevelup(Skills.SkillType skill, float level)`
(Player.cs:2616) fires only on a level-up, purely cosmetic in vanilla
(spawns `m_skillLevelupEffects`) — a clean "a level was just crossed,
here's the new level" signal with no side effects to worry about
breaking.

`Game.m_skillGainRate` (Game.cs) is `public static float
m_skillGainRate = 1f;`, a world-modifier-driven multiplier (see
`death-and-respawn.md` §6 for the sibling `m_skillReductionRate` and how
`UpdateWorldRates` feeds these from `GlobalKeys.SkillGainRate` /
`"skillgainrate"`) — it scales every `Raise()` call uniformly, so it
does not change what a *single* on-screen XP-gain number should mean,
only how fast it accumulates.

## 6. Hook points, with Mono-inlining trade-offs

Per `death-and-respawn.md` §9a and the project's prior experience,
Harmony patches on small, non-virtual, single-call-site methods risk
Mono inlining them away at IL level, silently no-opping the patch. Sizes
below are from the bodies quoted above.

1. **Intercepting a top-left message** — patch
   `MessageHud.ShowMessage(MessageType, string, int, Sprite, bool, bool)`.
   Public, non-trivial (localizes, branches on a 2-case switch, mutates
   queue/log state) — safe from inlining, and per §1 it is the single
   point every TopLeft message passes through regardless of source
   (local `Player.Message`, RPC relay, `MessageAll`). A `Prefix`
   filtering `type == MessageType.TopLeft`, doing our own
   identity-keyed insert/update, calling `AddLog` ourselves if the log
   feature matters, and returning `false` to skip vanilla's enqueue is
   the correct single hook. *Trade-off:* suppressing entirely means our
   list must reimplement the localize-then-log behaviour vanilla did
   inline; alternative is a `Postfix`-only approach that lets vanilla's
   single slot keep running invisibly while we read its queued
   `MsgData` for our own list — more code for no benefit, since we
   already have to intercept for the "return false" suppression.

2. **Knowing when an item is picked up** — patch
   `Humanoid.Pickup(GameObject, bool, bool)`, not
   `Character.ShowPickupMessage`. `Pickup` is large (network-aware,
   inventory mutation, `ZNetScene.Destroy`, equip logic) — safe.
   `ShowPickupMessage` is a two-statement forwarding method on a base
   class with one call site in the whole assembly, structurally
   identical in shape to the tiny `ZInput.GetButton*` wrappers that were
   bypassed by inlining before — treat it as unsafe to patch directly.
   A `Postfix` on `Pickup`, guarded `__instance == Player.m_localPlayer`
   and the method's own `bool` return, can re-read
   `go.GetComponent<ItemDrop>().m_itemData` — **note `go` is passed to
   `ZNetScene.instance.Destroy(go)` inside the method before the
   postfix runs**, so capture the `ItemDrop`/`ItemData` reference via a
   `Prefix` (before destroy) or, more simply, via `___component`/local
   capture through `Traverse`, or just re-derive from the Prefix's
   captured `ItemDrop.ItemData` (the managed object survives
   `Destroy`-ing its `GameObject`; only re-fetching the component from
   `go` afterward is unsafe). Simplest: `Prefix` captures
   `go.GetComponent<ItemDrop>()?.m_itemData` into a field passed to a
   `Postfix` via `__state`, then the `Postfix` uses `__result` (`Pickup`
   returns `bool` success) to decide whether to notify.

3. **Knowing when XP is gained** — patch `Skills.RaiseSkill(SkillType,
   float)`. Public, multi-statement, calls into `GetSkill`/`Raise`/
   `RebalanceSkills`/`Message`/`Gogan.LogEvent` — safe from inlining.
   A `Prefix` capturing the target `Skill`'s `m_level`/`m_accumulator`
   before the call (via `GetSkillList()`, since `GetSkill` is private)
   into `__state`, and a `Postfix` reading them after, gives: the exact
   XP delta (`newAccumulator - oldAccumulator`, or `+GetNextLevelRequirement()`
   worth if a level rolled over and `m_accumulator` reset to 0), whether
   a level was crossed (`newLevel > oldLevel`, matching `Raise()`'s own
   `bool`, which is otherwise not exposed), and the resulting level.
   *Trade-off vs. patching `Skill.Raise` directly:* `Raise` is a
   smaller, single-call-site instance method (one caller: `RaiseSkill`)
   — same inlining risk profile as `ShowPickupMessage`; prefer
   `RaiseSkill` for the same reason as item #2. `Player.OnSkillLevelup`
   is a safe secondary hook (public, one purpose, called only on an
   actual level-up) if only the "level crossed" moment is wanted without
   computing a delta.

## Things I could not verify from the assemblies

1. **`SkillDef.m_increseStep` per-skill values** — code default `1f`;
   the shipped `SkillDef` assets (one per `Skills.SkillType`) almost
   certainly carry different per-skill values (e.g. a weapon skill vs.
   Sneak likely differ). Asset data; read at runtime off each
   `Skills.SkillDef.m_increseStep` (reachable via
   `Player.m_localPlayer.GetComponent<Skills>().m_skills`, a public
   `List<SkillDef>`) if an exact number is needed.
2. **`MessageHud` prefab hierarchy / canvas placement** — only the
   public field names above are confirmed from code; the actual
   screen-space anchoring of `m_messageText`/`m_messageIcon` (their
   `RectTransform` anchors, what they're parented under, whether
   `Hud.m_rootObject` or a dedicated canvas) needs runtime inspection,
   the same caveat `death-and-respawn.md` §7 makes for `EnemyHud`'s
   sibling markers.
3. **`Skills.GetSkillList()`'s exact public signature** — referenced
   here by name and expected shape (`public List<Skill> GetSkillList()`)
   based on it being the only public means of reaching a `Skill`
   instance by skill type given `GetSkill(SkillType)` is private;
   re-verify the exact signature before writing a patch against it.
4. **Whether `MessageHud.ShowMessage`'s `Prefix` needs to replicate
   `Hud.IsUserHidden()`'s early-return** — vanilla's own early return
   (`if (Hud.IsUserHidden() && !showDespiteHiddenHUD) return;`) happens
   inside the method our `Prefix` would intercept before reaching it;
   a suppressing `Prefix` must reproduce that check itself or our list
   will keep showing entries while the vanilla HUD is hidden.
