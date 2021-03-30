﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Huobi.Client.Websocket.Client;
using Huobi.Client.Websocket.Config;
using Huobi.Client.Websocket.Messages.Account.Factories;
using Huobi.Client.Websocket.Messages.MarketData.Pulling.MarketByPrice;
using Huobi.Client.Websocket.Messages.MarketData.Pulling.MarketCandlestick;
using Huobi.Client.Websocket.Messages.MarketData.Pulling.MarketDepth;
using Huobi.Client.Websocket.Messages.MarketData.Pulling.MarketDetails;
using Huobi.Client.Websocket.Messages.MarketData.Pulling.MarketTradeDetail;
using Huobi.Client.Websocket.Messages.MarketData.Subscription;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketBestBidOffer;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketByPrice;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketCandlestick;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketDepth;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketDetails;
using Huobi.Client.Websocket.Messages.MarketData.Subscription.MarketTradeDetail;
using Huobi.Client.Websocket.Messages.MarketData.Ticks;
using Huobi.Client.Websocket.Messages.MarketData.Values;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Huobi.Client.Websocket.Sample
{
    public class Program
    {
        private static readonly ManualResetEvent _exitEvent = new(false);

        public static async Task Main()
        {
            // setup DI
            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.dev.json", true);

            var configuration = configurationBuilder.Build();
            var clientConfig = configuration.GetSection("HuobiWebsocketClient");

            var serviceCollection = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole())
                .AddSingleton(configuration)
                .Configure<HuobiWebsocketClientConfig>(clientConfig)
                .AddHuobiWebsocketServices();

            var serviceProvider = serviceCollection
                .BuildServiceProvider();

            // configure console logging
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("Starting application");

            using var client = serviceProvider.GetRequiredService<IHuobiWebsocketClient>();

            client.Streams.UnhandledMessageStream.Subscribe(x => { logger.LogError($"Unhandled message: {x}"); });
            client.Streams.ErrorMessageStream.Subscribe(
                x => { logger.LogError($"Error message received! Code: {x.ErrorCode}; Message: {x.Message}"); });

            client.Streams.SubscribeResponseStream.Subscribe(x => { logger.LogInformation($"Subscribed to topic: {x.Topic}"); });
            client.Streams.UnsubscribeResponseStream.Subscribe(
                x => { logger.LogInformation($"Unsubscribed from topic: {x.Topic}"); });

            client.Streams.MarketCandlestickUpdateStream.Subscribe(
                msg =>
                {
                    logger.LogInformation(
                        $"Market candlestick update {msg.Topic} | [amount={msg.Tick.Amount}] [open={msg.Tick.Open}] [close={msg.Tick.Close}] [low={msg.Tick.Low}] [high={msg.Tick.High}] [vol={msg.Tick.Vol}] [count={msg.Tick.Count}]");
                });

            client.Streams.MarketCandlestickPullStream.Subscribe(
                msg =>
                {
                    for (var i = 0; i < msg.Data.Length; ++i)
                    {
                        var item = msg.Data[i];
                        logger.LogInformation(
                            $"Market candlestick pull {msg.Topic} | [amount={item.Amount}] [open={item.Open}] [close={item.Close}] [low={item.Low}] [high={item.High}] [vol={item.Vol}] [count={item.Count}]");
                    }
                });

            client.Streams.MarketDepthUpdateStream.Subscribe(
                msg =>
                {
                    for (var i = 0; i < msg.Tick.Bids.Length; ++i)
                    {
                        var bid = msg.Tick.Bids[i];
                        var ask = msg.Tick.Asks[i];

                        logger.LogInformation(
                            $"Market depth update {msg.Topic} | [bid {i}: price={bid[0]} size={bid[1]}] [ask {i}: price={ask[0]} size={ask[1]}]");
                    }
                });

            client.Streams.MarketDepthPullStream.Subscribe(
                msg =>
                {
                    for (var i = 0; i < msg.Data.Bids.Length; ++i)
                    {
                        var bid = msg.Data.Bids[i];
                        var ask = msg.Data.Asks[i];

                        logger.LogInformation(
                            $"Market depth update {msg.Topic} | [bid {i}: price={bid[0]} size={bid[1]}] [ask {i}: price={ask[0]} size={ask[1]}]");
                    }
                });

            client.Streams.MarketByPriceUpdateStream.Subscribe(msg => { HandleMarketByPriceTick(msg, logger); });
            client.Streams.MarketByPriceRefreshUpdateStream.Subscribe(msg => { HandleMarketByPriceTick(msg, logger); });
            client.Streams.MarketByPricePullStream.Subscribe(msg => { HandleMarketByPricePullTick(msg, logger); });

            client.Streams.MarketBestBidOfferUpdateStream.Subscribe(
                msg =>
                {
                    logger.LogInformation(
                        $"Market best bid/offer update {msg.Topic} | [symbol={msg.Tick.Symbol}] [quoteTime={msg.Tick.QuoteTime}] [bid={msg.Tick.Bid}] [bidSize={msg.Tick.BidSize}] [ask={msg.Tick.Ask}] [askSize={msg.Tick.AskSize}] [seqId={msg.Tick.SeqId}]");
                });

            client.Streams.MarketTradeDetailUpdateStream.Subscribe(
                msg =>
                {
                    for (var i = 0; i < msg.Tick.Data.Length; ++i)
                    {
                        var item = msg.Tick.Data[i];

                        logger.LogInformation(
                            $"Market trade detail update {msg.Topic}, id={msg.Tick.Id}, ts={msg.Tick.Timestamp} | [item {i}: amount={item.Amount} ts={item.Timestamp} id={item.Id} tradeId={item.TradeId} price={item.Price} direction={item.Direction}]");
                    }
                });

            client.Streams.MarketTradeDetailPullStream.Subscribe(
                msg =>
                {
                    for (var i = 0; i < msg.Data.Length; ++i)
                    {
                        var item = msg.Data[i];

                        logger.LogInformation(
                            $"Market trade detail pull {msg.Topic} | [item {i}: amount={item.Amount} ts={item.Timestamp} id={item.Id} tradeId={item.TradeId} price={item.Price} direction={item.Direction}]");
                    }
                });

            client.Streams.MarketDetailsUpdateStream.Subscribe(
                msg =>
                {
                    logger.LogInformation(
                        $"Market details update {msg.Topic} | [amount={msg.Tick.Amount}] [open={msg.Tick.Open}] [close={msg.Tick.Close}] [low={msg.Tick.Low}] [high={msg.Tick.High}] [vol={msg.Tick.Vol}] [count={msg.Tick.Count}]");
                });

            client.Streams.MarketDetailsPullStream.Subscribe(
                msg =>
                {
                    logger.LogInformation(
                        $"Market details pull {msg.Topic} | [amount={msg.Data.Amount}] [open={msg.Data.Open}] [close={msg.Data.Close}] [low={msg.Data.Low}] [high={msg.Data.High}] [vol={msg.Data.Vol}] [count={msg.Data.Count}]");
                });

            await client.Communicator.Start();

            await Task.Delay(1000);

            var authenticationRequestFactory = serviceProvider.GetRequiredService<IHuobiAuthenticationRequestFactory>();

            var authenticationRequest = authenticationRequestFactory.CreateRequest();
            client.Send(authenticationRequest);

            //var marketCandlestickSubscribeRequest = new MarketCandlestickSubscribeRequest(
            //    "btcusdt",
            //    MarketCandlestickPeriodType.OneMinute,
            //    "id1");
            //client.Send(marketCandlestickSubscribeRequest);

            //var marketDepthSubscribeRequest =
            //    new MarketDepthSubscribeRequest("btcusdt", MarketDepthStepType.NoAggregation, "id1");
            //client.Send(marketDepthSubscribeRequest);

            //var marketByPriceSubscribeRequest = new MarketByPriceSubscribeRequest("btcusdt", MarketByPriceLevelType.Five, "id1");
            //client.Send(marketByPriceSubscribeRequest);

            //var marketByPriceRefreshSubscribeRequest = new MarketByPriceRefreshSubscribeRequest(
            //    "btcusdt",
            //    MarketByPriceRefreshLevelType.Five,
            //    "id1");
            //client.Send(marketByPriceRefreshSubscribeRequest);

            //var marketBestBidOfferSubscribeRequest = new MarketBestBidOfferSubscribeRequest("btcusdt", "id1");
            //client.Send(marketBestBidOfferSubscribeRequest);

            //var marketTradeDetailSubscribeRequest = new MarketTradeDetailSubscribeRequest("btcusdt", "id1");
            //client.Send(marketTradeDetailSubscribeRequest);

            //var marketDetailsSubscribeRequest = new MarketDetailsSubscribeRequest("btcusdt", "id1");
            //client.Send(marketDetailsSubscribeRequest);

            //await Task.Delay(1000);

            //var now = new ZonedDateTime(Instant.FromDateTimeOffset(DateTimeOffset.UtcNow), DateTimeZone.Utc);
            //var marketCandlestickPullRequest = new MarketCandlestickPullRequest(
            //    "btcusdt",
            //    MarketCandlestickPeriodType.SixtyMinutes,
            //    "id1",
            //    now.PlusHours(-5),
            //    now.PlusHours(-2));
            //client.Send(marketCandlestickPullRequest);

            //var marketDepthPullRequest = new MarketDepthPullRequest(
            //    "btcusdt",
            //    MarketDepthStepType.NoAggregation,
            //    "id1");
            //client.Send(marketDepthPullRequest);

            //var marketByPricePullRequest = new MarketByPricePullRequest(
            //    "btcusdt",
            //    MarketByPriceLevelType.Five,
            //    "id1");
            //client.Send(marketByPricePullRequest);

            //var marketTradeDetailPullRequest = new MarketTradeDetailPullRequest(
            //    "btcusdt",
            //    "id1");
            //client.Send(marketTradeDetailPullRequest);

            //var marketDetailsPullRequest = new MarketDetailsPullRequest(
            //    "btcusdt",
            //    "id1");
            //client.Send(marketDetailsPullRequest);

            //await Task.Delay(5000);

            //var candlestickUnsubscribeRequest = new MarketCandlestickUnsubscribeRequest(
            //    "btcusdt",
            //    MarketCandlestickPeriodType.OneMinute,
            //    "id1");
            //client.Send(candlestickUnsubscribeRequest);

            //var depthUnsubscribeRequest = new MarketDepthUnsubscribeRequest("btcusdt", MarketDepthStepType.NoAggregation, "id1");
            //client.Send(depthUnsubscribeRequest);

            //var marketByPriceUnsubscribeRequest =
            //    new MarketByPriceUnsubscribeRequest("btcusdt", MarketByPriceLevelType.Five, "id1");
            //client.Send(marketByPriceUnsubscribeRequest);

            //var marketByPriceRefreshUnsubscribeRequest = new MarketByPriceRefreshUnsubscribeRequest(
            //    "btcusdt",
            //    MarketByPriceRefreshLevelType.Five,
            //    "id1");
            //client.Send(marketByPriceRefreshUnsubscribeRequest);

            //var marketBestBidOfferUnsubscribeRequest = new MarketBestBidOfferUnsubscribeRequest("btcusdt", "id1");
            //client.Send(marketBestBidOfferUnsubscribeRequest);

            //var marketTradeDetailUnsubscribeRequest = new MarketTradeDetailUnsubscribeRequest("btcusdt", "id1");
            //client.Send(marketTradeDetailUnsubscribeRequest);

            //var marketDetailsUnsubscribeRequest = new MarketDetailsUnsubscribeRequest("btcusdt", "id1");
            //client.Send(marketDetailsUnsubscribeRequest);

            _exitEvent.WaitOne();
        }

        private static void HandleMarketByPriceTick(UpdateMessage<MarketByPriceTick> message, ILogger<Program> logger)
        {
            if (message.Tick.Bids != null)
            {
                for (var i = 0; i < message.Tick.Bids.Length; ++i)
                {
                    var bid = message.Tick.Bids[i];

                    logger.LogInformation($"Market by price update {message.Topic} | [bid {i}: price={bid[0]} size={bid[1]}]");
                }
            }

            if (message.Tick.Asks != null)
            {
                for (var i = 0; i < message.Tick.Asks.Length; ++i)
                {
                    var bid = message.Tick.Asks[i];

                    logger.LogInformation($"Market by price update {message.Topic} | [ask {i}: price={bid[0]} size={bid[1]}]");
                }
            }
        }

        private static void HandleMarketByPricePullTick(MarketByPricePullResponse response, ILogger<Program> logger)
        {
            for (var i = 0; i < response.Data.Bids.Length; ++i)
            {
                var bid = response.Data.Bids[i];

                logger.LogInformation($"Market by price pull {response.Topic} | [bid {i}: price={bid[0]} size={bid[1]}]");
            }

            for (var i = 0; i < response.Data.Asks.Length; ++i)
            {
                var bid = response.Data.Asks[i];

                logger.LogInformation($"Market by price pull {response.Topic} | [ask {i}: price={bid[0]} size={bid[1]}]");
            }
        }
    }
}