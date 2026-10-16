using System.Globalization;
using IndexerCore.Data;
using IndexerCore.Data.Entities;
using IndexerCore.Models.Items;
using IndexerCore.Monero;
using IndexerCore.Protocol.Messages;
using IndexerCore.Protocol.Sales;
using IndexerCore.Services.Items;
using IndexerCore.Services.Sales;
using Microsoft.Extensions.Options;

namespace IndexerCore.Services.Listings;

public sealed record PendingListing(string TransactionId, string CollectionId, string MarketplaceId,
    string SellerOwnerKey, string PriceAtomic, string Mode, string Status, bool CanBuy, ItemResponse Item)
{
    public DateTimeOffset? FirstSeenAt { get; init; }
}

public sealed class PendingListingQuery(IndexerDbContext db, SecondaryTradeHandler trades,
    ItemQueryService items, IOptions<MoneroOptions> options)
{
    public async Task<PendingListing?> ValidateAsync(MoneroTransaction candidate, long nextHeight, CancellationToken token)
    {
        var data = XtopMessageReader.ExtractMessage(candidate.Extra);
        if (data == null) return null;
        var envelope = XtopMessageReader.ReadMessage(data, options.Value.XtopNetwork);
        if (!ListingModes.IsListing(envelope.Operation)) return null;
        var message = new Message { Data = data, Version = envelope.Version, Network = options.Value.XtopNetwork, Operation = envelope.Operation };
        message.Transaction = new Transaction
        {
            Hash = Convert.FromHexString(candidate.Id), NativeData = candidate.NativeData, Message = message,
            Block = new Block { Network = options.Value.XtopNetwork, Height = nextHeight, Hash = new byte[32], PreviousHash = new byte[32] }
        };
        var result = await trades.ValidateAsync(message, envelope, token);
        if (result?.MarketplaceId == null) return null;
        var itemId = result.PreviousOutput.ItemId!;
        var owner = Convert.ToHexStringLower(result.PreviousOutput.OwnerKey);
        db.ChangeTracker.Clear();
        var item = (await items.GetAsync(itemId, token))?.Item;
        if (item == null || item.Burn != null || item.Listing != null || item.OwnerKey != owner) return null;
        return new(candidate.Id, item.CollectionId, Convert.ToHexStringLower(result.MarketplaceId), owner,
            result.Price!.Value.ToString("0", CultureInfo.InvariantCulture), ListingModes.Name(envelope.Operation), "submitted", false, item);
    }
}
