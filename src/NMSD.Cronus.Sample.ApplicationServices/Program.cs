﻿using System.Linq;
using System.Reflection;
using System.Threading;
using NMSD.Cronus.DomainModelling;
using NMSD.Cronus.EventSourcing;
using NMSD.Cronus.Persistence.MSSQL.Config;
using NMSD.Cronus.Pipeline.Config;
using NMSD.Cronus.Pipeline.Hosts;
using NMSD.Cronus.Pipeline.Transport.RabbitMQ.Config;
using NMSD.Cronus.Sample.Collaboration.Users;
using NMSD.Cronus.Sample.Collaboration.Users.Commands;
using NMSD.Cronus.Sample.IdentityAndAccess.Accounts;
using NMSD.Cronus.Sample.IdentityAndAccess.Accounts.Commands;

namespace NMSD.Cronus.Sample.ApplicationService
{
    class Program
    {
        static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            UseCronusHost();
            System.Console.WriteLine("Started command handlers");
            System.Console.ReadLine();
        }

        static void UseCronusHost()
        {
            var cfg = new CronusConfiguration();

            const string IAA = "IdentityAndAccess";
            cfg.ConfigureEventStore<MsSqlEventStoreSettings>(eventStore =>
            {
                eventStore
                    .SetConnectionStringName("cronus-es")
                    .SetAggregateStatesAssembly(Assembly.GetAssembly(typeof(AccountState)));
            });
            cfg.PipelineEventStorePublisher(publisher =>
            {
                publisher.UseTransport<RabbitMq>();
            });
            cfg.ConfigureConsumer<EndpointCommandConsumableSettings>(IAA, consumer =>
            {
                consumer.MessagesAssemblies = new[] { Assembly.GetAssembly(typeof(RegisterAccount)) };
                consumer.RegisterAllHandlersInAssembly(Assembly.GetAssembly(typeof(AccountAppService)), (type, context) =>
                    {
                        var handler = FastActivator.CreateInstance(type, null);
                        var repositoryHandler = handler as IAggregateRootApplicationService;
                        if (repositoryHandler != null)
                        {
                            repositoryHandler.Repository = new RabbitRepository((IAggregateRepository)cfg.GlobalSettings.EventStores.Single(es => es.BoundedContext == IAA), cfg.GlobalSettings.EventStorePublisher);
                        }
                        return handler;
                    });
                consumer.UseTransport<RabbitMq>();
            });

            const string Collaboration = "Collaboration";
            cfg.ConfigureEventStore<MsSqlEventStoreSettings>(eventStore =>
            {
                eventStore
                    .SetConnectionStringName("cronus-es")
                    .SetAggregateStatesAssembly(Assembly.GetAssembly(typeof(UserState)));
            });
            cfg.ConfigureConsumer<EndpointCommandConsumableSettings>(Collaboration, consumer =>
            {
                consumer.MessagesAssemblies = new[] { Assembly.GetAssembly(typeof(CreateUser)) };
                consumer.RegisterAllHandlersInAssembly(Assembly.GetAssembly(typeof(UserAppService)), (type, context) =>
                    {
                        var handler = FastActivator.CreateInstance(type, null);
                        var repositoryHandler = handler as IAggregateRootApplicationService;
                        if (repositoryHandler != null)
                        {
                            repositoryHandler.Repository = new RabbitRepository((IAggregateRepository)cfg.GlobalSettings.EventStores.Single(es => es.BoundedContext == Collaboration), cfg.GlobalSettings.EventStorePublisher);
                        }
                        return handler;
                    });
                consumer.UseTransport<RabbitMq>();
            })
            .Build();

            new CronusHost(cfg).Start();
        }
    }
}