# #Reclamation Project - Institute uplink implant

institute-agent-name = Institute agent
institute-agent-objective = Serve the Institute. Follow the orders of the Director and your Division Head.
institute-uplink-unknown = Unknown

store-currency-display-research-point = Research Points
store-preset-name-institute-uplink = Institute uplink

store-category-institute-network = Network
store-category-institute-weapons = Weapons
store-category-institute-armor = Armor & MOD suits
store-category-institute-infiltration = Infiltration
store-category-institute-implants = Implants
store-category-institute-supplies = Field supplies
store-category-institute-command = Command
store-category-institute-reinforcements = Reinforcements

institute-listing-network-name = Open the Institute network
institute-listing-network-desc = Free. Send Research Points to other members. The Director and Division Heads also manage PINs and objectives here.

institute-uplink-chat-wrap = [color=#4fd2e0][bold]Institute:[/bold] {$message}[/color]
institute-uplink-welcome = You are an Institute agent. Your uplink implant is the button in your action bar. Your PIN is in your character menu (C).
institute-uplink-briefing = You are an Institute agent. The Director and your Division Head give you objectives; finishing them earns Research Points for your uplink.
institute-uplink-briefing-pin = Your uplink PIN: {$pin} (it also unlocks your Institute tablet)

institute-uplink-pin-title = Institute uplink
institute-uplink-pin-prompt = Enter your 4-digit PIN. It is in your character menu (C).
institute-uplink-pin-submit = Unlock
institute-uplink-pin-wrong = Wrong PIN. The Director has been told.
institute-uplink-pin-alert = Wrong uplink PIN entered by {$name}.
institute-tablet-pin-alert = Wrong tablet PIN entered by {$name}.
institute-tablet-pin-title = Institute tablet
institute-tablet-pin-prompt = This tablet is locked. Enter its owner's 4-digit PIN.
institute-tablet-unlocked = Tablet unlocked. Use it again to open it.
institute-uplink-pin-reset = {$by} changed your uplink PIN. Check your character menu (C) for the new one.

institute-network-title = Institute network
institute-network-balance = Research Points: {$amount}
institute-network-refresh = Refresh
institute-network-send-heading = Send Research Points
institute-network-nobody = No other Institute members.
institute-network-nobody-managed = Nobody you can give orders to yet.
institute-network-amount = Amount
institute-network-send = Send
institute-network-not-enough = You don't have that many Research Points.
institute-network-points-sent = Sent {$amount} Research Points to {$name}.
institute-network-points-received = {$name} sent you {$amount} Research Points.

institute-network-members-heading = Members
institute-network-reset-all = Reset all PINs
institute-network-reset-pin = Random PIN
institute-network-set-pin = Set PIN
institute-network-pin-current = PIN {$pin}
institute-network-pin-invalid = A PIN is exactly 4 digits.
institute-network-pin-set = Changed {$name}'s PIN.
institute-network-pins-reset = Reset {$count ->
    [one] 1 PIN
   *[other] {$count} PINs
}.
institute-network-no-objectives = No objectives.
institute-network-objective-line = {$title} | {$reward} RP | {$status}
institute-network-status-open = open
institute-network-status-done = done
institute-network-status-paid = paid
institute-network-force = Force complete
institute-network-remove = Remove

institute-network-assign-heading = Give an objective
institute-network-assign-to = Member
institute-network-assign-kind = Type
institute-network-assign-person = Target
institute-network-assign-item = Item
institute-network-assign-title = Title
institute-network-assign-text = Details
institute-network-assign-title-hint = Short order, e.g. Recover the holodisk from Goodneighbor
institute-network-assign-text-hint = Optional details
institute-network-assign-reward = Reward (RP)
institute-network-assign-give = Give objective
institute-network-pick-person = Pick a target first.
institute-network-pick-item = Pick an item first.
institute-network-need-title = Custom orders need a title.
institute-network-objective-failed = That objective could not be given.
institute-network-objective-given = Objective given: {$title}
institute-network-objective-removed = Objective removed: {$title}
institute-network-objective-removed-member = Objective withdrawn: {$title}
institute-network-objective-forced = Marked as complete: {$title}
institute-network-new-orders = New orders from {$by}: {$title} ({$reward} Research Points)
institute-network-objective-paid = Objective complete: {$title}. +{$reward} Research Points.

institute-objective-kind-steal = Steal an item
institute-objective-kind-kill = Kill a person
institute-objective-kind-protect = Protect a person
institute-objective-kind-survive = Stay alive and undetected
institute-objective-kind-custom = Custom order

institute-objective-kill-title = Kill {$targetName}, {CAPITALIZE($job)}
institute-objective-protect-title = Keep {$targetName}, {CAPITALIZE($job)} alive
institute-objective-steal-title = Steal: {$item}
institute-objective-steal-desc = Get a {$item} and keep it on you.
institute-objective-custom-desc = Orders from the Institute. Your commander marks this complete.

institute-reinforcement-refunded = Nobody answered the relay signal. {$amount} Research Points refunded.
ghost-role-information-institute-reinforcement-rules = You are an Institute agent. Follow the orders of the Director and the Institute member who called you in. Normal antag rules apply.
ghost-role-information-institute-synth-name = Institute Gen 3 synth
ghost-role-information-institute-synth-description = Relay in to support Institute agents on the surface.
ghost-role-information-institute-courser-name = Institute Courser
ghost-role-information-institute-courser-description = Relay in as a Courser to hunt down the Institute's targets.
