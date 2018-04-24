using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YesSql.Provider.SqlServer;
using YesSql.Sql;

namespace YesSql.Samples.Gating
{
    class Program
    {
        static void Main(string[] args)
        {
            var store = new Store(
                new Configuration()
                    .UseSqlServer(@"Data Source =.; Initial Catalog = yessql; Integrated Security = True")
                    .SetTablePrefix("Gating")
                );

            try
            {
                using (var session = store.CreateSession())
                {
                    new SchemaBuilder(session)
                        .DropMapIndexTable(nameof(PersonByName))
                        .DropTable("Identifiers")
                        .DropTable("Document");
                }
            }
            catch { }

            store.InitializeAsync().GetAwaiter().GetResult();

            using (var session = store.CreateSession())
            {
                var builder = new SchemaBuilder(session)
                    .CreateMapIndexTable(nameof(PersonByName), column => column
                        .Column<string>(nameof(PersonByName.SomeName))
                    );
            }

            store.RegisterIndexes<PersonIndexProvider>();

            Console.WriteLine("Creating content...");
            using (var session = store.CreateSession())
            {
                for (var i = 0; i < 10000; i++)
                {
                    session.Save(new Person() { Firstname = "Steve" + i });
                }
            }

            // Warmup
            Console.WriteLine("Warming up...");
            using (var session = store.CreateSession())
            {
                Task.Run(async () =>
                {
                    for (var i = 0; i < 500; i++)
                    {
                        await session.Query().For<Person>().With<PersonByName>(x => x.SomeName.StartsWith("Steve100")).ListAsync();
                        await session.Query().For<Person>().With<PersonByName>(x => x.SomeName == "Steve").ListAsync();
                        await session.Query().For<Person>().With<PersonByName>().Where(x => x.SomeName == "Steve").ListAsync();
                    }
                }).GetAwaiter().GetResult();
            }

            store.Configuration.DisableQueryGating();

            var concurrency = 32;
            var counter = 0;
            var MaxTransactions = 50000;
            var stopping = false;

            var tasks = Enumerable.Range(1, concurrency).Select(i => Task.Run(async () =>
            {
                Console.WriteLine($"Starting thread {i}");

                await Task.Delay(100);

                while (!stopping && Interlocked.Add(ref counter, 1) < MaxTransactions)
                {
                    using (var session = store.CreateSession())
                    {
                        await session.Query().For<Person>().With<PersonByName>(x => x.SomeName.StartsWith("Steve100")).ListAsync();
                        await session.Query().For<Person>().With<PersonByName>(x => x.SomeName == "Steve").ListAsync();
                        await session.Query().For<Person>().With<PersonByName>().Where(x => x.SomeName == "Steve").ListAsync();
                    }
                }
            })).ToList();

            tasks.Add(Task.Delay(TimeSpan.FromSeconds(3)));

            Task.WhenAny(tasks).GetAwaiter().GetResult();

            // Flushing tasks
            stopping = true;
            Task.WhenAll(tasks).GetAwaiter().GetResult();
            stopping = false;

            Console.WriteLine(counter);
        }
    }
}
