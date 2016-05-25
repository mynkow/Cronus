﻿using Elders.Cronus.Middleware;
using System;
using System.Collections.Generic;
using Elders.Cronus.Logging;
using System.Linq;

namespace Elders.Cronus.MessageProcessingMiddleware
{
    public class CronusMessageProcessorMiddleware : Middleware<List<TransportMessage>, IFeedResult>, IMessageProcessor
    {
        static readonly ILog log = LogProvider.GetLogger(typeof(CronusMessageProcessorMiddleware));

        MessageSubscriptionsMiddleware messageSubscriptionsMiddleware;

        public CronusMessageProcessorMiddleware(string name, MessageSubscriptionsMiddleware messageSubscriptionsMiddleware)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            Name = name;
            this.messageSubscriptionsMiddleware = messageSubscriptionsMiddleware;
        }

        public string Name { get; private set; }

        protected override IFeedResult Invoke(MiddlewareContext<List<TransportMessage>, IFeedResult> middlewareControl)
        {
            IFeedResult feedResult = FeedResult.Empty();
            var messages = middlewareControl.Context;
            try
            {
                messages.ForEach(msg =>
                {
                    var messageFeedResult = PerMessageUnitOfWork(msg);
                    feedResult = feedResult.With(messageFeedResult);
                });
            }
            catch (Exception ex)
            {
                feedResult = feedResult.AppendUnitOfWorkError(messages, ex);
            }
            return feedResult;
        }

        private IFeedResult PerMessageUnitOfWork(TransportMessage message)
        {
            IFeedResult feedResult = FeedResult.Empty();
            try
            {
                var handlerIds = from feedError in message.Errors
                                 let isUnitOfWorkError = message.Errors.Any(x => x.Origin.Type == ErrorOriginType.UnitOfWork)
                                 where feedError.Origin.Type == ErrorOriginType.MessageHandler && !isUnitOfWorkError
                                 select feedError.Origin.Id.ToString();

                var messageType = message.Payload.Payload.GetType();

                var subscribers = messageSubscriptionsMiddleware.Invoke(messageType);


                if (handlerIds.Count() > 0)
                    subscribers = subscribers.Where(subscription => handlerIds.Contains(subscription.Id));

                var subscriberList = subscribers.ToList();
                if (subscriberList.Count == 0)
                    log.WarnFormat("There is no handler/subscriber for {0}", message.Payload);

                subscriberList.ForEach(subscriber =>
                {
                    var handlerFeedResult = PerHandlerUnitOfWork(subscriber, message);
                    feedResult = feedResult.With(handlerFeedResult);
                });
            }
            catch (Exception ex)
            {
                feedResult = feedResult.AppendUnitOfWorkError(new List<TransportMessage>() { message }, ex);
            }
            return feedResult;
        }

        private IFeedResult PerHandlerUnitOfWork(SubscriberMiddleware subscriber, TransportMessage message)
        {
            var feedResult = FeedResult.Empty();
            try
            {
                subscriber.Invoke(message.Payload);
                feedResult = feedResult.AppendSuccess(message);
            }
            catch (Exception ex)
            {
                feedResult = feedResult.AppendError(message, new FeedError()
                {
                    Origin = new ErrorOrigin(subscriber.Id, ErrorOriginType.MessageHandler),
                    Error = new SerializableException(ex)
                });
            }

            return feedResult;
        }

        public IEnumerable<SubscriberMiddleware> GetSubscriptions()
        {
            return messageSubscriptionsMiddleware.GetSubscriptions();
        }
    }
}