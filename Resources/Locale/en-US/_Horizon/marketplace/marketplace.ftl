marketplace-item-slot-name = Marketplace tray

## Window

marketplace-window-title = Sector Marketplace
marketplace-header-title = [font size=16][bold][color=#7CC4FF]SECTOR[/color] [color=#E8EEF7]MARKETPLACE[/color][/bold][/font]
marketplace-header-terminal = [color={$mutedColor}]Terminal:[/color] [color={$accentColor}]{$grid}[/color]  [color={$mutedColor}]· Right click or Shift+click an item to examine it[/color]

marketplace-stat-balance = [color={$mutedColor}]Balance[/color]  [bold][color={$priceColor}]{$balance}[/color][/bold]
marketplace-stat-no-account = [color=#FF6B6B]No bank account[/color]
marketplace-stat-tax = [color={$mutedColor}]Tax[/color]  [bold]{$tax}%[/bold]
marketplace-stat-listings = [color={$mutedColor}]Listings[/color]  [bold]{$count}[/bold]
marketplace-stat-pending = [color={$mutedColor}]Pending payout[/color]  [bold][color=#FFD866]{$amount}[/color][/bold]

marketplace-tab-market = Market
marketplace-tab-sell = Sell
marketplace-tab-mine = My listings

marketplace-search-placeholder = Search: item, seller, station or #listing
marketplace-sort-label = Sort:
marketplace-sort-newest = Newest first
marketplace-sort-price-asc = Price: low to high
marketplace-sort-price-desc = Price: high to low
marketplace-sort-name = Name
marketplace-sort-count = Quantity
marketplace-empty = There are no listings on the market yet.
marketplace-empty-search = Nothing found.
marketplace-mine-empty = You have no active listings.

marketplace-examine-hint = Right click or Shift+click to examine

## Listing card

marketplace-card-price-unit = [bold][color={$priceColor}]{$price}[/color][/bold] [color={$mutedColor}]each[/color]
marketplace-card-price-single = [bold][color={$priceColor}]{$price}[/color][/bold]
marketplace-card-stock = [color={$mutedColor}]In stock:[/color] {$count}
marketplace-card-seller = [color={$mutedColor}]Seller:[/color] {$seller}
marketplace-card-id = [color={$mutedColor}]ID card:[/color] {$idName}
marketplace-card-station = [color={$mutedColor}]Station:[/color] [color={$accentColor}]{$grid}[/color]  [color={$mutedColor}]· Listing #{$id}[/color]
marketplace-card-total = [color={$mutedColor}]Total:[/color] [bold][color={$color}]{$total}[/color][/bold]
marketplace-card-own = [color={$accentColor}]Your listing[/color]
marketplace-card-amount = Qty:
marketplace-card-buy = Buy
marketplace-card-withdraw = Withdraw

## Selling

marketplace-sell-slot-title = Item in terminal
marketplace-sell-slot-empty = [color={$mutedColor}]The tray is empty[/color]
marketplace-sell-slot-hint = [color={$mutedColor}]Put an item into the console to list it. Stackable resources can be sold in parts.[/color]
marketplace-sell-available = [color={$mutedColor}]Available:[/color] {$count}
marketplace-sell-eject = Eject item
marketplace-sell-title = New listing
marketplace-sell-amount = Quantity
marketplace-sell-all = All
marketplace-sell-price-unit = Price per unit
marketplace-sell-price-single = Price
marketplace-sell-sum = Sale total
marketplace-sell-tax = Tax ({$tax}%)
marketplace-sell-receive = You receive
marketplace-sell-merge = [color={$accentColor}]Will be merged into your listing #{$id} (currently {$count}) - same resource and price.[/color]
marketplace-sell-button = List on the market
marketplace-sell-note = [color={$mutedColor}]The {$tax}% tax is only taken when the item sells. Listings can be withdrawn at any time and are cleared at the end of the shift.[/color]

## Messages

marketplace-no-id = No ID card
marketplace-unknown-grid = Unknown station

marketplace-error-no-item = There is no item in the tray.
marketplace-error-no-account = You don't have a bank account.
marketplace-error-bad-price = The price must be between 1 and {$max}.
marketplace-error-forbidden = This item can't be listed.
marketplace-error-too-many-listings = You can't have more than {$max} listings at once.
marketplace-error-failed = Failed to list the item.
marketplace-error-no-listing = This listing is no longer available.
marketplace-error-not-owner = This is not your listing.
marketplace-error-price-changed = The listing's price changed, check it again.
marketplace-error-own-listing = You can't buy your own listing.
marketplace-error-bad-amount = You can buy between 1 and {$max}.
marketplace-error-too-expensive = That deal is too large, lower the quantity.
marketplace-error-insufficient-funds = Insufficient funds: {$amount} needed.

marketplace-popup-listed = {$item} listed on the market (listing #{$id}).
marketplace-popup-merged = {$item} added to your listing #{$id}.
marketplace-popup-withdrawn = Listing #{$id} withdrawn.
marketplace-popup-bought = Bought {$item} ×{$amount} for {$total}.
marketplace-popup-sold = Sold {$item} ×{$amount}. Received {$payout} (tax {$tax}).
marketplace-popup-sold-pending = Sold {$item} ×{$amount}. {$payout} is waiting for you at any marketplace console.
marketplace-popup-pending-paid = Marketplace earnings received: {$amount}.

## Requests

marketplace-tab-requests = Requests
marketplace-request-search-placeholder = Search: wanted item, requester, station or #request
marketplace-request-only-mine = Only mine
marketplace-request-empty = Nobody is looking for anything yet. Post the first request!

marketplace-request-create-title = New request
marketplace-request-field-title = What you need
marketplace-request-field-title-placeholder = For example: plasteel for plating
marketplace-request-field-description = Description
marketplace-request-field-quantity = Quantity
marketplace-request-field-reward = Reward for the whole order
marketplace-request-summary-held = [color={$mutedColor}]Held from your account:[/color] [bold]{$reward}[/bold]
marketplace-request-summary-payout = [color={$mutedColor}]Supplier receives:[/color] [bold][color={$priceColor}]{$payout}[/color][/bold] [color={$mutedColor}]({$tax}% tax)[/color]
marketplace-request-create-button = Post request
marketplace-request-note = [color={$mutedColor}]The reward is withdrawn now and held until the order is fulfilled. It is refunded if you cancel or the shift ends.[/color]

marketplace-request-card-meta = [color={$mutedColor}]Wanted:[/color] {$quantity}  [color={$mutedColor}]· Offers:[/color] {$offers}  [color={$mutedColor}]· Request #{$id}[/color]
marketplace-request-card-reward = [color={$mutedColor}]reward[/color]
marketplace-request-card-requester = [color={$mutedColor}]Requester:[/color] {$name}  [color={$mutedColor}]· ID:[/color] {$idName}  [color={$mutedColor}]· Station:[/color] [color={$accentColor}]{$grid}[/color]
marketplace-request-card-no-offers = [color={$mutedColor}]Your request. No offers yet.[/color]
marketplace-request-card-offers-header = [color={$accentColor}]Your request.[/color] Offers ({$count}) - accept the one you like:
marketplace-request-cancel = Cancel request

marketplace-offer-button = Offer
marketplace-offer-accept = Accept
marketplace-offer-reject = Reject
marketplace-offer-withdraw = Withdraw
marketplace-offer-take-back = Take back
marketplace-offer-hint-empty = [color={$mutedColor}]Put an item into the console tray to offer it.[/color]
marketplace-offer-hint-item = [color={$mutedColor}]In tray:[/color] [color={$accentColor}]{$item}[/color]
marketplace-offer-hint-already = [color={$accentColor}]You already made an offer on this request.[/color]
marketplace-offer-row-offerer = [color={$mutedColor}]From:[/color] {$name}  [color={$mutedColor}]· ID:[/color] {$idName}  [color={$mutedColor}]· Station:[/color] [color={$accentColor}]{$grid}[/color]
marketplace-offer-row-request = [color={$mutedColor}]For request:[/color] {$title}  [color={$mutedColor}]· reward[/color] [color={$priceColor}]{$reward}[/color]
marketplace-offer-status-pending = [color=#FFD866]Waiting for the requester[/color]
marketplace-offer-status-returned = [color=#FF8A6B]Rejected - take your item back[/color]

marketplace-my-offers-title = My offers
marketplace-my-offers-empty = You haven't offered anything yet.

marketplace-request-error-title = The request title must be between 1 and {$max} characters.
marketplace-request-error-quantity = The quantity must be between 1 and {$max}.
marketplace-request-error-too-many = You can't have more than {$max} open requests.
marketplace-request-error-missing = This request no longer exists.
marketplace-request-error-not-owner = This is not your request.
marketplace-request-error-own = You can't make an offer on your own request.
marketplace-request-error-too-many-offers = This request already has {$max} offers.
marketplace-request-error-too-many-own-offers = You can't have more than {$max} offers at once.
marketplace-offer-error-missing = This offer is no longer available.
marketplace-offer-error-not-owner = This is not your offer.

marketplace-request-popup-created = Request "{$title}" posted, {$reward} held.
marketplace-request-popup-cancelled = Request "{$title}" cancelled, {$reward} refunded.
marketplace-offer-popup-made = {$item} offered for "{$title}".
marketplace-offer-popup-received = New offer on your request "{$title}": {$item}.
marketplace-offer-popup-accepted = Deal done: you received {$item} for "{$title}".
marketplace-offer-popup-paid = Your offer for "{$title}" was accepted! Received {$payout} (tax {$tax}).
marketplace-offer-popup-paid-pending = Your offer for "{$title}" was accepted! {$payout} is waiting for you at any marketplace console.
marketplace-offer-popup-rejected = Offer "{$item}" rejected.
marketplace-offer-popup-withdrawn = {$item} returned to you.

marketplace-error-contraband = Class 2 and higher contraband can't be traded on the marketplace.
marketplace-error-contraband-inside = There is class 2 or higher contraband inside this item - take it out before selling.

## Black market

marketplace-black-window-title = Black Market
marketplace-black-header-title = [font size=16][bold][color=#FF4D57]BLACK[/color] [color=#F2E4E6]MARKET[/color][/bold][/font]  [color=#A38A8E]· encrypted channel[/color]
marketplace-anon-name = Anonymous {$code}
marketplace-anon-id = [encrypted]
marketplace-anon-grid = Cipher {$code}
