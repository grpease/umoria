using Umoria.Core.Character;
using Umoria.Core.Config;
using Umoria.Core.Data.Generated;
using Umoria.Core.Dungeon;
using Umoria.Core.Items;
using Umoria.Core.Model.Generated;
using Umoria.Core.Rng;

namespace Umoria.Core.Stores;

// src/store.cpp -- entering, command interpreter, buying, selling. (M13)
//
// Real call graph (read, not assumed):
//   - Haggling is game logic all the way down: the owner's comments are
//     random picks (RNG), the counter-offers draw randomNumber(5), the
//     insult counter and good/bad purchase record persist in the store,
//     and being thrown out closes the store until dg.game_turn + 2500 +
//     randomNumber(2500). Every prompt reads through getKeyInput(), so each
//     one zeroes game.command_count.
//   - The offer parser (storeGetHaggle()) keeps store.cpp's file-static
//     store_last_increment between offers (PlayerWorld.StoreLastIncrement)
//     and has quirks kept as written: `increment` is not reset between
//     re-prompts, so an empty line with no increment set followed by "7"
//     adds 7 instead of offering 7; "0" or text that isn't a number
//     re-prompts; stringToNumber() leaves the old value on bad input.
//   - The store screen's text (item list, prices, gold, prompts, the
//     haggle lines) is written through PlayerWorld.ScreenWrite: what it
//     shows is computed here (prices are charisma-adjusted at display time;
//     displaySingleCost() prints a haggled price without the clamp or width
//     displayStoreInventory() uses), the drawing is the UI's (M14).
//     clearScreen()/eraseLine()/moveCursor() and message_ready_to_print
//     are display only and left out.
//   - Leaving the store calls drawCavePanel() (PY_STUDY upkeep,
//     PlayerCore.DrawCavePanel); the i/e/t/w/x keys run the real inventory
//     commands (Items.InventoryCommands) until no command is left open.
//
// Not modelled: getStringInput() with text typed before <ESCAPE> (the
// original keeps it in the offer buffer, which the parser then reads; the
// ReadString hook only returns a whole line or null).
public static class StoreCommands
{
    // The status of the customer bid.
    // Note: a received bid may still result in a rejected offer.
    private enum BidState
    {
        Received = 0, // the big was received successfully
        Rejected, // the bid was rejected, or cancelled by the customer
        Offended, // customer tried to sell an undesirable item
        Insulted, // the store owner was insulted too many times by the bid
    }

    // src/store.cpp:17 storeInitializeOwners() -- initializes the stores
    // with owners -RAK-
    public static void InitializeOwners(PlayerWorld w)
    {
        var count = StoreConfig.MaxOwners / StoreConfig.MaxStores;

        for (var storeId = 0; storeId < StoreConfig.MaxStores; storeId++)
        {
            var store = w.Stores[storeId];

            store.OwnerId = (byte)(StoreConfig.MaxStores * (RandomNumber.Next(count) - 1) + storeId);
            store.InsultsCounter = 0;
            store.TurnsLeftBeforeClosing = 0;
            store.UniqueItemsCounter = 0;
            store.GoodPurchases = 0;
            store.BadPurchases = 0;

            for (var i = 0; i < store.Inventory.Length; i++)
            {
                store.Inventory[i] = Store.EmptyRecord();
            }
        }
    }

    // ---- screen ------------------------------------------------------------

    // putString()
    private static void Put(PlayerWorld w, int row, int col, string text) => w.ScreenWrite?.Invoke(row, col, text, false);

    // putStringClearToEOL()
    private static void PutLine(PlayerWorld w, int row, int col, string text) => w.ScreenWrite?.Invoke(row, col, text, true);

    // ---- comments vary. -RAK- ---------------------------------------------

    // Comment one : Finished haggling
    private static void PrintSpeechFinishedHaggling(PlayerWorld w)
    {
        w.Print(DataStoreOwnersData.SpeechSaleAccepted[RandomNumber.Next(14) - 1]);
    }

    // %A1 is offer, %A2 is asking.
    private static void PrintSpeechSellingHaggle(PlayerWorld w, int offer, int asking, int final)
    {
        var comment = final > 0
            ? DataStoreOwnersData.SpeechSellingHaggleFinal[RandomNumber.Next(3) - 1]
            : DataStoreOwnersData.SpeechSellingHaggle[RandomNumber.Next(16) - 1];

        comment = Helpers.InsertNumberIntoString(comment, "%A1", offer, false);
        comment = Helpers.InsertNumberIntoString(comment, "%A2", asking, false);
        w.Print(comment);
    }

    private static void PrintSpeechBuyingHaggle(PlayerWorld w, int offer, int asking, int final)
    {
        var comment = final > 0
            ? DataStoreOwnersData.SpeechBuyingHaggleFinal[RandomNumber.Next(3) - 1]
            : DataStoreOwnersData.SpeechBuyingHaggle[RandomNumber.Next(15) - 1];

        comment = Helpers.InsertNumberIntoString(comment, "%A1", offer, false);
        comment = Helpers.InsertNumberIntoString(comment, "%A2", asking, false);
        w.Print(comment);
    }

    // Kick 'da bum out. -RAK-
    private static void PrintSpeechGetOutOfMyStore(PlayerWorld w)
    {
        var comment = RandomNumber.Next(5) - 1;
        w.Print(DataStoreOwnersData.SpeechInsultedHagglingDone[comment]);
        w.Print(DataStoreOwnersData.SpeechGetOutOfMyStore[comment]);
    }

    private static void PrintSpeechTryAgain(PlayerWorld w)
    {
        w.Print(DataStoreOwnersData.SpeechHagglingTryAgain[RandomNumber.Next(10) - 1]);
    }

    private static void PrintSpeechSorry(PlayerWorld w)
    {
        w.Print(DataStoreOwnersData.SpeechSorry[RandomNumber.Next(5) - 1]);
    }

    // ---- display -----------------------------------------------------------

    // Displays the set of commands -RAK-
    private static void DisplayStoreCommands(PlayerWorld w)
    {
        PutLine(w, 20, 0, "You may:");
        PutLine(w, 21, 0, " p) Purchase an item.           b) Browse store's inventory.");
        PutLine(w, 22, 0, " s) Sell an item.               i/e/t/w/x) Inventory/Equipment Lists.");
        PutLine(w, 23, 0, "ESC) Exit from Building.        ^R) Redraw the screen.");
    }

    // Displays the set of commands -RAK-
    private static void DisplayStoreHaggleCommands(PlayerWorld w, int haggleType)
    {
        PutLine(w, 21, 0, haggleType == -1 ? "Specify an asking-price in gold pieces." : "Specify an offer in gold pieces.");
        PutLine(w, 22, 0, "ESC) Quit Haggling.");
        // (eraseLine(23): clear last line)
    }

    // Displays a store's inventory -RAK-
    private static void DisplayStoreInventory(PlayerWorld w, Store store, int itemPosStart)
    {
        var itemPosEnd = ((itemPosStart / 12) + 1) * 12;
        if (itemPosEnd > store.UniqueItemsCounter)
        {
            itemPosEnd = store.UniqueItemsCounter;
        }

        int itemLineNum;

        for (itemLineNum = itemPosStart % 12; itemPosStart < itemPosEnd; itemLineNum++)
        {
            // (the original sets the count to 1 for the description and
            // restores it after; a copy does the same)
            var item = store.Inventory[itemPosStart].Item;

            if (InventoryActions.ItemSingleStackable(item))
            {
                item.ItemsCount = 1;
            }

            var description = ItemDescription.Describe(w.Identification, item, true);

            PutLine(w, itemLineNum + 5, 0, Helpers.Truncate($"{(char)('a' + itemLineNum)}) {description}", Helpers.MoriaObjDescSize));

            var cost = store.Inventory[itemPosStart].Cost;

            string msg;
            if (cost <= 0)
            {
                var value = -cost;
                value = value * PlayerStatLogic.StatAdjustmentCharisma(w.Py) / 100;
                if (value <= 0)
                {
                    value = 1;
                }
                msg = $"{value,9}";
            }
            else
            {
                msg = $"{cost,9} [Fixed]";
            }

            PutLine(w, itemLineNum + 5, 59, msg);
            itemPosStart++;
        }

        // (lines below the last item are erased)

        if (store.UniqueItemsCounter > 12)
        {
            Put(w, 17, 60, "- cont. -");
        }
        // (else eraseLine(17, 60))
    }

    // Re-displays only a single cost -RAK-
    private static void DisplaySingleCost(PlayerWorld w, int storeId, int itemId)
    {
        var cost = w.Stores[storeId].Inventory[itemId].Cost;

        string msg;
        if (cost < 0)
        {
            var c = -cost;
            c = c * PlayerStatLogic.StatAdjustmentCharisma(w.Py) / 100;
            msg = $"{c}";
        }
        else
        {
            msg = $"{cost,9} [Fixed]";
        }
        PutLine(w, (itemId % 12) + 5, 59, msg);
    }

    // Displays players gold -RAK-
    private static void DisplayPlayerRemainingGold(PlayerWorld w)
    {
        PutLine(w, 18, 17, $"Gold Remaining : {w.Py.Misc.Au}");
    }

    // Displays store -RAK-
    private static void DisplayStore(PlayerWorld w, Store store, string ownerName, int currentTopItemId)
    {
        // (clearScreen())
        Put(w, 3, 9, ownerName);
        Put(w, 4, 3, "Item");
        Put(w, 4, 60, "Asking Price");
        DisplayPlayerRemainingGold(w);
        DisplayStoreCommands(w);
        DisplayStoreInventory(w, store, currentTopItemId);
    }

    // ---- prompts -----------------------------------------------------------

    // Get the ID of a store item and return it's value -RAK-
    // Returns true if the item was found.
    private static bool StoreGetItemId(PlayerWorld w, out int itemId, string prompt, int itemPosStart, int itemPosEnd)
    {
        itemId = -1;
        var itemFound = false;

        var msg = $"(Items {(char)(itemPosStart + 'a')}-{(char)(itemPosEnd + 'a')}, ESC to exit) {prompt}";

        while (PlayerCore.GetCommand(w, msg, out var keyChar))
        {
            var key = keyChar - 'a';
            if (key >= itemPosStart && key <= itemPosEnd)
            {
                itemFound = true;
                itemId = key;
                break;
            }
            // (terminalBellSound())
        }
        // (messageLineClear())

        return itemFound;
    }

    // Increase the insult counter and get angry if too many -RAK-
    private static bool StoreIncreaseInsults(PlayerWorld w, int storeId)
    {
        var store = w.Stores[storeId];

        store.InsultsCounter++;

        if (store.InsultsCounter <= DataStoreOwnersData.StoreOwners[store.OwnerId].MaxInsults)
        {
            return false;
        }

        // customer angered the store owner with too many insults!
        PrintSpeechGetOutOfMyStore(w);
        store.InsultsCounter = 0;
        store.BadPurchases++;
        store.TurnsLeftBeforeClosing = w.Dg.GameTurn + 2500 + RandomNumber.Next(2500);

        return true;
    }

    // Decrease insults -RAK-
    private static void StoreDecreaseInsults(PlayerWorld w, int storeId)
    {
        if (w.Stores[storeId].InsultsCounter != 0)
        {
            w.Stores[storeId].InsultsCounter--;
        }
    }

    // Have insulted while haggling -RAK-
    // Returns true if the store owner was angered.
    private static bool StoreHaggleInsults(PlayerWorld w, int storeId)
    {
        if (StoreIncreaseInsults(w, storeId))
        {
            return true;
        }

        PrintSpeechTryAgain(w);

        // keep insult separate from rest of haggle
        // (printMessage(CNIL): flushes the message line, no game state)

        return false;
    }

    // Returns true if the customer made a valid offer
    private static bool StoreGetHaggle(PlayerWorld w, string prompt, ref int newOffer, int offerCount)
    {
        var validOffer = true;

        if (offerCount == 0)
        {
            w.StoreLastIncrement = 0;
        }

        var increment = false;
        var adjustment = 0;

        var startLen = prompt.Length;

        // (the offer buffer lives across re-prompts, as in the original)
        var msg = "";

        // Get a customers new offer
        while (validOffer && adjustment == 0)
        {
            PutLine(w, 0, 0, prompt);

            if (offerCount != 0 && w.StoreLastIncrement != 0)
            {
                var absStoreLastIncrement = Math.Abs((int)w.StoreLastIncrement);

                PutLine(w, 0, startLen, Helpers.Truncate($"[{(w.StoreLastIncrement < 0 ? '-' : '+')}{absStoreLastIncrement}] ", Helpers.MoriaMessageSize));
            }

            // (getStringInput() reads through getKeyInput(), which zeroes
            // game.command_count)
            w.Game.CommandCount = 0;
            var input = w.ReadString(prompt, 40);
            if (input is null)
            {
                // customer aborted, i.e. pressed escape
                validOffer = false;
            }
            else
            {
                msg = input;
            }

            var p = 0;
            while (p < msg.Length && msg[p] == ' ')
            {
                // fast forward to next space character
                p++;
            }
            if (p < msg.Length && (msg[p] == '+' || msg[p] == '-'))
            {
                increment = true;
            }

            if (offerCount != 0 && increment)
            {
                Helpers.StringToNumber(msg, ref adjustment);

                // Don't accept a zero here.  Turn off increment if it was zero
                // because a zero will not exit.  This can be zero if the user
                // did not type a number after the +/- sign.
                if (adjustment == 0)
                {
                    increment = false;
                }
                else
                {
                    w.StoreLastIncrement = (short)adjustment;
                }
            }
            else if (offerCount != 0 && msg.Length == 0)
            {
                adjustment = w.StoreLastIncrement;
                increment = true;
            }
            else
            {
                Helpers.StringToNumber(msg, ref adjustment);
            }

            // don't allow incremental haggling, if player has not made an offer yet
            if (validOffer && offerCount == 0 && increment)
            {
                w.Print("You haven't even made your first offer yet!");
                adjustment = 0;
                increment = false;
            }
        }

        if (validOffer)
        {
            if (increment)
            {
                newOffer += adjustment;
            }
            else
            {
                newOffer = adjustment;
            }
        }
        // (else messageLineClear())

        return validOffer;
    }

    private static BidState StoreReceiveOffer(PlayerWorld w, int storeId, string prompt, ref int newOffer, int lastOffer, int offerCount, int factor)
    {
        var status = BidState.Received;

        var done = false;
        while (!done)
        {
            if (StoreGetHaggle(w, prompt, ref newOffer, offerCount))
            {
                // customer submitted valid offer
                if (newOffer * factor >= lastOffer * factor)
                {
                    done = true;
                }
                else if (StoreHaggleInsults(w, storeId))
                {
                    // customer angered the store owner!
                    status = BidState.Insulted;
                    done = true;
                }
                else
                {
                    // new_offer rejected, reset new_offer so that incremental
                    // haggling works correctly
                    newOffer = lastOffer;
                }
            }
            else
            {
                // customer aborted offer
                status = BidState.Rejected;
                done = true;
            }
        }

        return status;
    }

    // ---- buying ------------------------------------------------------------

    private static void StorePurchaseCustomerAdjustment(PlayerWorld w, ref int minSell, ref int maxSell)
    {
        var charisma = PlayerStatLogic.StatAdjustmentCharisma(w.Py);

        maxSell = maxSell * charisma / 100;
        if (maxSell <= 0)
        {
            maxSell = 1;
        }

        minSell = minSell * charisma / 100;
        if (minSell <= 0)
        {
            minSell = 1;
        }
    }

    // Haggling routine -RAK-
    private static BidState StorePurchaseHaggle(PlayerWorld w, int storeId, out int price, in Inventory item)
    {
        var status = BidState.Received;

        var newPrice = 0;

        var store = w.Stores[storeId];
        var owner = DataStoreOwnersData.StoreOwners[store.OwnerId];

        var cost = StoreInventory.SellPrice(w, store, out var minSell, out var maxSell, item);

        StorePurchaseCustomerAdjustment(w, ref minSell, ref maxSell);

        // cast max_inflate to signed so that subtraction works correctly
        var maxBuy = cost * (200 - owner.MaxInflate) / 100;
        if (maxBuy <= 0)
        {
            maxBuy = 1;
        }

        DisplayStoreHaggleCommands(w, 1);

        var finalAskingPrice = minSell;
        var currentAskingPrice = maxSell;

        var comment = "Asking";
        var acceptedWithoutHaggle = false;
        var offersCount = 0; // this prevents incremental haggling on first try

        // go right to final price if player has bargained well
        if (StoreNoNeedToBargain(store, finalAskingPrice))
        {
            w.Print("After a long bargaining session, you agree upon the price.");
            currentAskingPrice = minSell;
            comment = "Final offer";
            acceptedWithoutHaggle = true;

            // Set up automatic increment, so that a return will accept the final price.
            w.StoreLastIncrement = (short)minSell;
            offersCount = 1;
        }

        var minOffer = maxBuy;
        var lastOffer = minOffer;
        var newOffer = 0;

        var minPer = (int)owner.HagglesPer;
        var maxPer = minPer * 3;

        var finalFlag = 0;

        var rejected = false;
        bool biddingOpen;

        while (!rejected)
        {
            do
            {
                biddingOpen = true;

                Put(w, 1, 0, Helpers.Truncate($"{comment} :  {currentAskingPrice}", Helpers.MoriaMessageSize));

                status = StoreReceiveOffer(w, storeId, "What do you offer? ", ref newOffer, lastOffer, offersCount, 1);

                if (status != BidState.Received)
                {
                    rejected = true;
                }
                else
                {
                    // review the received bid

                    if (newOffer > currentAskingPrice)
                    {
                        PrintSpeechSorry(w);

                        // rejected, reset new_offer for incremental haggling
                        newOffer = lastOffer;

                        // If the automatic increment is large enough to overflow,
                        // then the player must have made a mistake.  Clear it
                        // because it is useless.
                        if (lastOffer + w.StoreLastIncrement > currentAskingPrice)
                        {
                            w.StoreLastIncrement = 0;
                        }
                    }
                    else if (newOffer == currentAskingPrice)
                    {
                        rejected = true;
                        newPrice = newOffer;
                    }
                    else
                    {
                        biddingOpen = false;
                    }
                }
            } while (!rejected && biddingOpen);

            if (!rejected)
            {
                var adjustment = (newOffer - lastOffer) * 100 / (currentAskingPrice - lastOffer);

                if (adjustment < minPer)
                {
                    rejected = StoreHaggleInsults(w, storeId);
                    if (rejected)
                    {
                        status = BidState.Insulted;
                    }
                }
                else if (adjustment > maxPer)
                {
                    adjustment = adjustment * 75 / 100;
                    if (adjustment < maxPer)
                    {
                        adjustment = maxPer;
                    }
                }

                adjustment = ((currentAskingPrice - newOffer) * (adjustment + RandomNumber.Next(5) - 3) / 100) + 1;

                // don't let the price go up
                if (adjustment > 0)
                {
                    currentAskingPrice -= adjustment;
                }

                if (currentAskingPrice < finalAskingPrice)
                {
                    currentAskingPrice = finalAskingPrice;
                    comment = "Final Offer";

                    // Set the automatic haggle increment so that RET will give
                    // a new_offer equal to the final_asking_price price.
                    w.StoreLastIncrement = (short)(finalAskingPrice - newOffer);
                    finalFlag++;

                    if (finalFlag > 3)
                    {
                        status = StoreIncreaseInsults(w, storeId) ? BidState.Insulted : BidState.Rejected;
                        rejected = true;
                    }
                }
                else if (newOffer >= currentAskingPrice)
                {
                    rejected = true;
                    newPrice = newOffer;
                }

                if (!rejected)
                {
                    lastOffer = newOffer;
                    offersCount++; // enable incremental haggling

                    // (eraseLine(1, 0))
                    Put(w, 1, 39, Helpers.Truncate($"Your last offer : {lastOffer}", Helpers.MoriaMessageSize));

                    PrintSpeechSellingHaggle(w, lastOffer, currentAskingPrice, finalFlag);

                    // If the current increment would take you over the store's
                    // price, then decrease it to an exact match.
                    if (currentAskingPrice - lastOffer < w.StoreLastIncrement)
                    {
                        w.StoreLastIncrement = (short)(currentAskingPrice - lastOffer);
                    }
                }
            }
        }

        // update bargaining info
        if (status == BidState.Received && !acceptedWithoutHaggle)
        {
            StoreUpdateBargainingSkills(store, newPrice, finalAskingPrice);
        }

        price = newPrice; // update callers price before returning

        return status;
    }

    // ---- selling -----------------------------------------------------------

    private static void StoreSellCustomerAdjustment(PlayerWorld w, StoreOwner owner, ref int cost, out int minBuy, out int maxBuy, out int maxSell)
    {
        cost = cost * (200 - PlayerStatLogic.StatAdjustmentCharisma(w.Py)) / 100;
        cost = cost * (200 - DataStoresData.RaceGoldAdjustments[owner.Race][w.Py.Misc.RaceId]) / 100;
        if (cost < 1)
        {
            cost = 1;
        }

        maxSell = cost * owner.MaxInflate / 100;

        // cast max_inflate to signed so that subtraction works correctly
        maxBuy = cost * (200 - owner.MaxInflate) / 100;
        minBuy = cost * (200 - owner.MinInflate) / 100;
        if (minBuy < 1)
        {
            minBuy = 1;
        }
        if (maxBuy < 1)
        {
            maxBuy = 1;
        }
        if (minBuy < maxBuy)
        {
            minBuy = maxBuy;
        }
    }

    // Haggling routine -RAK-
    private static BidState StoreSellHaggle(PlayerWorld w, int storeId, out int price, in Inventory item)
    {
        var status = BidState.Received;

        var newPrice = 0;

        var store = w.Stores[storeId];
        var cost = ItemValue.StoreItemValue(w.Identification, item);

        var rejected = false;

        var maxGold = 0;
        var minPer = 0;
        var maxPer = 0;
        var maxSell = 0;
        var minBuy = 0;
        var maxBuy = 0;

        if (cost < 1)
        {
            status = BidState.Offended;
            rejected = true;
        }
        else
        {
            var owner = DataStoreOwnersData.StoreOwners[store.OwnerId];

            StoreSellCustomerAdjustment(w, owner, ref cost, out minBuy, out maxBuy, out maxSell);

            minPer = owner.HagglesPer;
            maxPer = minPer * 3;
            maxGold = owner.MaxCost;
        }

        var finalAskingPrice = 0;

        var acceptedWithoutHaggle = false;

        if (!rejected)
        {
            DisplayStoreHaggleCommands(w, -1);

            var offerCount = 0; // this prevents incremental haggling on first try

            int currentAskingPrice;
            int finalFlag;
            string comment;

            if (maxBuy > maxGold)
            {
                finalFlag = 1;
                comment = "Final Offer";

                // Disable the automatic haggle increment on RET.
                w.StoreLastIncrement = 0;
                currentAskingPrice = maxGold;
                finalAskingPrice = maxGold;
                w.Print("I am sorry, but I have not the money to afford such a fine item.");
                acceptedWithoutHaggle = true;
            }
            else
            {
                finalFlag = 0;
                currentAskingPrice = maxBuy;
                finalAskingPrice = minBuy;

                if (finalAskingPrice > maxGold)
                {
                    finalAskingPrice = maxGold;
                }

                comment = "Offer";

                // go right to final price if player has bargained well
                if (StoreNoNeedToBargain(store, finalAskingPrice))
                {
                    w.Print("After a long bargaining session, you agree upon the price.");
                    currentAskingPrice = finalAskingPrice;
                    comment = "Final offer";
                    acceptedWithoutHaggle = true;

                    // Set up automatic increment, so that a return
                    // will accept the final price.
                    w.StoreLastIncrement = (short)finalAskingPrice;
                    offerCount = 1;
                }
            }

            var minOffer = maxSell;
            var lastOffer = minOffer;
            var newOffer = 0;

            if (currentAskingPrice < 1)
            {
                currentAskingPrice = 1;
            }

            bool biddingOpen;

            do
            {
                do
                {
                    biddingOpen = true;

                    Put(w, 1, 0, Helpers.Truncate($"{comment} :  {currentAskingPrice}", Helpers.MoriaMessageSize));

                    status = StoreReceiveOffer(w, storeId, "What price do you ask? ", ref newOffer, lastOffer, offerCount, -1);

                    if (status != BidState.Received)
                    {
                        rejected = true;
                    }
                    else
                    {
                        // review the received bid

                        if (newOffer < currentAskingPrice)
                        {
                            PrintSpeechSorry(w);

                            // rejected, reset new_offer for incremental haggling
                            newOffer = lastOffer;

                            // If the automatic increment is large enough to
                            // overflow, then the player must have made a mistake.
                            // Clear it because it is useless.
                            if (lastOffer + w.StoreLastIncrement < currentAskingPrice)
                            {
                                w.StoreLastIncrement = 0;
                            }
                        }
                        else if (newOffer == currentAskingPrice)
                        {
                            rejected = true;
                            newPrice = newOffer;
                        }
                        else
                        {
                            biddingOpen = false;
                        }
                    }
                } while (!rejected && biddingOpen);

                if (!rejected)
                {
                    var adjustment = (lastOffer - newOffer) * 100 / (lastOffer - currentAskingPrice);

                    if (adjustment < minPer)
                    {
                        rejected = StoreHaggleInsults(w, storeId);
                        if (rejected)
                        {
                            status = BidState.Insulted;
                        }
                    }
                    else if (adjustment > maxPer)
                    {
                        adjustment = adjustment * 75 / 100;
                        if (adjustment < maxPer)
                        {
                            adjustment = maxPer;
                        }
                    }

                    adjustment = ((newOffer - currentAskingPrice) * (adjustment + RandomNumber.Next(5) - 3) / 100) + 1;

                    // don't let the price go down
                    if (adjustment > 0)
                    {
                        currentAskingPrice += adjustment;
                    }

                    if (currentAskingPrice > finalAskingPrice)
                    {
                        currentAskingPrice = finalAskingPrice;
                        comment = "Final Offer";

                        // Set the automatic haggle increment so that RET will give
                        // a new_offer equal to the final_asking_price price.
                        w.StoreLastIncrement = (short)(finalAskingPrice - newOffer);
                        finalFlag++;

                        if (finalFlag > 3)
                        {
                            status = StoreIncreaseInsults(w, storeId) ? BidState.Insulted : BidState.Rejected;
                            rejected = true;
                        }
                    }
                    else if (newOffer <= currentAskingPrice)
                    {
                        rejected = true;
                        newPrice = newOffer;
                    }

                    if (!rejected)
                    {
                        lastOffer = newOffer;
                        offerCount++; // enable incremental haggling

                        // (eraseLine(1, 0))
                        Put(w, 1, 39, Helpers.Truncate($"Your last bid {lastOffer}", Helpers.MoriaMessageSize));

                        PrintSpeechBuyingHaggle(w, currentAskingPrice, lastOffer, finalFlag);

                        // If the current decrement would take you under the store's
                        // price, then increase it to an exact match.
                        if (currentAskingPrice - lastOffer > w.StoreLastIncrement)
                        {
                            w.StoreLastIncrement = (short)(currentAskingPrice - lastOffer);
                        }
                    }
                }
            } while (!rejected);
        }

        // update bargaining info
        if (status == BidState.Received && !acceptedWithoutHaggle)
        {
            StoreUpdateBargainingSkills(store, newPrice, finalAskingPrice);
        }

        price = newPrice; // update callers price before returning

        return status;
    }

    // ---- the commands ------------------------------------------------------

    // Get the number of store items to display on the screen
    private static int StoreItemsToDisplay(int storeCounter, int currentTopItemId)
    {
        if (currentTopItemId == 12)
        {
            return storeCounter - 1 - 12;
        }

        if (storeCounter > 11)
        {
            return 11;
        }

        return storeCounter - 1;
    }

    // Buy an item from a store -RAK-
    // Returns true is the owner kicks out the customer
    private static bool StorePurchaseAnItem(PlayerWorld w, int storeId, ref int currentTopItemId)
    {
        var kickCustomer = false; // don't kick them out of the store!

        var store = w.Stores[storeId];
        var py = w.Py;

        if (store.UniqueItemsCounter < 1)
        {
            w.Print("I am currently out of stock.");
            return false;
        }

        var itemCount = StoreItemsToDisplay(store.UniqueItemsCounter, currentTopItemId);
        if (!StoreGetItemId(w, out var itemId, "Which item are you interested in? ", 0, itemCount))
        {
            return false;
        }

        // Get the item number to be bought

        itemId += currentTopItemId; // true item_id

        var sellItem = InventoryActions.TakeOneItem(store.Inventory[itemId].Item);

        if (!PlayerInventory.CanCarryItemCount(py, w.Identification, sellItem))
        {
            PutLine(w, 0, 0, "You cannot carry that many different items.");
            return false;
        }

        var status = BidState.Received;
        int price;

        if (store.Inventory[itemId].Cost > 0)
        {
            price = store.Inventory[itemId].Cost;
        }
        else
        {
            status = StorePurchaseHaggle(w, storeId, out price, sellItem);
        }

        if (status == BidState.Insulted)
        {
            kickCustomer = true;
        }
        else if (status == BidState.Received)
        {
            if (py.Misc.Au >= price)
            {
                PrintSpeechFinishedHaggling(w);
                StoreDecreaseInsults(w, storeId);
                py.Misc.Au -= price;

                var newItemId = PlayerInventory.CarryItem(py, w.Identification, sellItem);
                int savedStoreCounter = store.UniqueItemsCounter;

                StoreInventory.DestroyItem(w, storeId, itemId, true);

                var description = ItemDescription.Describe(w.Identification, py.Inventory[newItemId], true);

                PutLine(w, 0, 0, Helpers.Truncate($"You have {description} ({(char)(newItemId + 'a')})", Helpers.MoriaObjDescSize));

                PlayerCore.Strength(w);

                if (currentTopItemId >= store.UniqueItemsCounter)
                {
                    currentTopItemId = 0;
                    DisplayStoreInventory(w, store, currentTopItemId);
                }
                else
                {
                    ref var storeItem = ref store.Inventory[itemId];

                    if (savedStoreCounter == store.UniqueItemsCounter)
                    {
                        if (storeItem.Cost < 0)
                        {
                            storeItem.Cost = price;
                            DisplaySingleCost(w, storeId, itemId);
                        }
                    }
                    else
                    {
                        DisplayStoreInventory(w, store, itemId);
                    }
                }
                DisplayPlayerRemainingGold(w);
            }
            else
            {
                if (StoreIncreaseInsults(w, storeId))
                {
                    kickCustomer = true;
                }
                else
                {
                    PrintSpeechFinishedHaggling(w);
                    w.Print("Liar!  You have not the gold!");
                }
            }
        }

        // Less intuitive, but looks better here than in storePurchaseHaggle.
        DisplayStoreCommands(w);
        // (eraseLine(1, 0))

        return kickCustomer;
    }

    // Functions to emulate the original Pascal sets
    private static bool SetGeneralStoreItems(int itemId) => itemId switch
    {
        TreasureCategory.TvDigging or TreasureCategory.TvBoots or TreasureCategory.TvCloak or TreasureCategory.TvFood or
            TreasureCategory.TvFlask or TreasureCategory.TvLight or TreasureCategory.TvSpike => true,
        _ => false,
    };

    private static bool SetArmoryItems(int itemId) => itemId switch
    {
        TreasureCategory.TvBoots or TreasureCategory.TvGloves or TreasureCategory.TvHelm or TreasureCategory.TvShield or
            TreasureCategory.TvHardArmor or TreasureCategory.TvSoftArmor => true,
        _ => false,
    };

    private static bool SetWeaponsmithItems(int itemId) => itemId switch
    {
        TreasureCategory.TvSlingAmmo or TreasureCategory.TvBolt or TreasureCategory.TvArrow or TreasureCategory.TvBow or
            TreasureCategory.TvHafted or TreasureCategory.TvPolearm or TreasureCategory.TvSword => true,
        _ => false,
    };

    private static bool SetTempleItems(int itemId) => itemId switch
    {
        TreasureCategory.TvHafted or TreasureCategory.TvScroll1 or TreasureCategory.TvScroll2 or TreasureCategory.TvPotion1 or
            TreasureCategory.TvPotion2 or TreasureCategory.TvPrayerBook => true,
        _ => false,
    };

    private static bool SetAlchemistItems(int itemId) => itemId switch
    {
        TreasureCategory.TvScroll1 or TreasureCategory.TvScroll2 or TreasureCategory.TvPotion1 or TreasureCategory.TvPotion2 => true,
        _ => false,
    };

    private static bool SetMagicShopItems(int itemId) => itemId switch
    {
        TreasureCategory.TvAmulet or TreasureCategory.TvRing or TreasureCategory.TvStaff or TreasureCategory.TvWand or
            TreasureCategory.TvScroll1 or TreasureCategory.TvScroll2 or TreasureCategory.TvPotion1 or TreasureCategory.TvPotion2 or
            TreasureCategory.TvMagicBook => true,
        _ => false,
    };

    // Each store will buy only certain items, based on TVAL
    public static readonly Func<int, bool>[] StoreBuy =
    [
        SetGeneralStoreItems, SetArmoryItems, SetWeaponsmithItems, SetTempleItems, SetAlchemistItems, SetMagicShopItems,
    ];

    // Sell an item to the store -RAK-
    // Returns true is the owner kicks out the customer
    private static bool StoreSellAnItem(PlayerWorld w, int storeId, ref int currentTopItemId)
    {
        var kickCustomer = false; // don't kick them out of the store!
        var py = w.Py;

        int firstItem = py.Pack.UniqueItems;
        var lastItem = -1;

        var mask = new bool[PlayerConfig.Equipment.Wield];

        for (var counter = 0; counter < py.Pack.UniqueItems; counter++)
        {
            var flag = StoreBuy[storeId](py.Inventory[counter].CategoryId);

            if (flag)
            {
                mask[counter] = true;

                if (counter < firstItem)
                {
                    firstItem = counter;
                }
                if (counter > lastItem)
                {
                    lastItem = counter;
                }
            }
            else
            {
                mask[counter] = false;
            }
        }

        if (lastItem == -1)
        {
            w.Print("You have nothing to sell to this store!");
            return false;
        }

        if (!PlayerCore.GetItemId(w, "Which one? ", firstItem, lastItem, mask, "I do not buy such items.", out var itemId))
        {
            return false;
        }

        var soldItem = InventoryActions.TakeOneItem(py.Inventory[itemId]);

        var description = ItemDescription.Describe(w.Identification, soldItem, true);

        w.Print(Helpers.Truncate($"Selling {description} ({(char)(itemId + 'a')})", Helpers.MoriaObjDescSize));

        if (!StoreInventory.CheckPlayerItemsCount(w.Stores[storeId], soldItem))
        {
            w.Print("I have not the room in my store to keep it.");
            return false;
        }

        var status = StoreSellHaggle(w, storeId, out var price, soldItem);

        if (status == BidState.Insulted)
        {
            kickCustomer = true;
        }
        else if (status == BidState.Offended)
        {
            w.Print("How dare you!");
            w.Print("I will not buy that!");
            kickCustomer = StoreIncreaseInsults(w, storeId);
        }
        else if (status == BidState.Received)
        {
            // bid received, and accepted!

            PrintSpeechFinishedHaggling(w);
            StoreDecreaseInsults(w, storeId);
            py.Misc.Au += price;

            // identify object in inventory to set objects_identified array
            w.Identification.ItemIdentify(w, ref itemId);

            // retake sold_item so that it will be identified
            soldItem = InventoryActions.TakeOneItem(py.Inventory[itemId]);

            // call spellItemIdentifyAndRemoveRandomInscription for store item, so charges/pluses are known
            w.Identification.SpellItemIdentifyAndRemoveRandomInscription(ref soldItem);
            InventoryDamage.DestroyItem(py, itemId);

            description = ItemDescription.Describe(w.Identification, soldItem, true);
            w.Print(Helpers.Truncate($"You've sold {description}", Helpers.MoriaObjDescSize));

            StoreInventory.CarryItem(w, storeId, out var itemPosId, soldItem);

            PlayerCore.Strength(w);

            var store = w.Stores[storeId];
            if (itemPosId >= 0)
            {
                if (itemPosId < 12)
                {
                    if (currentTopItemId < 12)
                    {
                        DisplayStoreInventory(w, store, itemPosId);
                    }
                    else
                    {
                        currentTopItemId = 0;
                        DisplayStoreInventory(w, store, currentTopItemId);
                    }
                }
                else if (currentTopItemId > 11)
                {
                    DisplayStoreInventory(w, store, itemPosId);
                }
                else
                {
                    currentTopItemId = 12;
                    DisplayStoreInventory(w, store, currentTopItemId);
                }
            }
            DisplayPlayerRemainingGold(w);
        }

        // Less intuitive, but looks better here than in storeSellHaggle.
        // (eraseLine(1, 0))
        DisplayStoreCommands(w);

        return kickCustomer;
    }

    // src/store.cpp:1097 storeEnter() -- entering a store -RAK-
    public static void Enter(PlayerWorld w, int storeId)
    {
        var store = w.Stores[storeId];

        if (store.TurnsLeftBeforeClosing >= w.Dg.GameTurn)
        {
            w.Print("The doors are locked.");
            return;
        }

        var currentTopItemId = 0;
        DisplayStore(w, store, DataStoreOwnersData.StoreOwners[store.OwnerId].Name, currentTopItemId);

        var exitStore = false;
        while (!exitStore)
        {
            // (moveCursor(20, 9); message_ready_to_print = false -- clear
            // the msg flag just like we do in dungeon.c)

            if (PlayerCore.GetCommand(w, "", out var command))
            {
                switch (command)
                {
                    case 'b':
                        if (currentTopItemId == 0)
                        {
                            if (store.UniqueItemsCounter > 12)
                            {
                                currentTopItemId = 12;
                                DisplayStoreInventory(w, store, currentTopItemId);
                            }
                            else
                            {
                                w.Print("Entire inventory is shown.");
                            }
                        }
                        else
                        {
                            currentTopItemId = 0;
                            DisplayStoreInventory(w, store, currentTopItemId);
                        }
                        break;
                    case 'E':
                    case 'e': // Equipment List
                    case 'I':
                    case 'i': // Inventory
                    case 'T':
                    case 't': // Take off
                    case 'W':
                    case 'w': // Wear
                    case 'X':
                    case 'x': // Switch weapon
                        int savedChr = w.Py.Stats.Used[PlayerConfig.Attr.Chr];

                        do
                        {
                            InventoryCommands.ExecuteCommand(w, command);
                            command = w.Game.DoingInventoryCommand;
                        } while (command != 0);

                        // redisplay store prices if charisma changes
                        if (savedChr != w.Py.Stats.Used[PlayerConfig.Attr.Chr])
                        {
                            DisplayStoreInventory(w, store, currentTopItemId);
                        }

                        w.Game.PlayerFreeTurn = false; // No free moves here. -CJS-
                        break;
                    case 'p':
                        exitStore = StorePurchaseAnItem(w, storeId, ref currentTopItemId);
                        break;
                    case 's':
                        exitStore = StoreSellAnItem(w, storeId, ref currentTopItemId);
                        break;
                    default:
                        // (terminalBellSound())
                        break;
                }
            }
            else
            {
                exitStore = true;
            }
        }

        // Can't save and restore the screen because inventoryExecuteCommand() does that.
        PlayerCore.DrawCavePanel(w);
    }

    // eliminate need to bargain if player has haggled well in the past -DJB-
    private static bool StoreNoNeedToBargain(Store store, int minPrice)
    {
        if (store.GoodPurchases == short.MaxValue)
        {
            return true;
        }

        var record = store.GoodPurchases - 3 * store.BadPurchases - 5;

        return record > 0 && record * record > minPrice / 50;
    }

    // update the bargain info -DJB-
    private static void StoreUpdateBargainingSkills(Store store, int price, int minPrice)
    {
        if (minPrice < 10)
        {
            return;
        }

        if (price == minPrice)
        {
            if (store.GoodPurchases < short.MaxValue)
            {
                store.GoodPurchases++;
            }
        }
        else
        {
            if (store.BadPurchases < short.MaxValue)
            {
                store.BadPurchases++;
            }
        }
    }
}
