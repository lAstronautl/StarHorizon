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
marketplace-sell-tax = Sector tax ({$tax}%)
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
