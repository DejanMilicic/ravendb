using System;
using System.Threading.Tasks;
using FastTests.Server.Documents.Queries.Parser;
using FastTests.Voron.Backups;
using FastTests.Voron.Compaction;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using SlowTests.Authentication;
using SlowTests.Bugs.MapRedue;
using SlowTests.Client;
using SlowTests.Client.Attachments;
using SlowTests.Issues;
using SlowTests.MailingList;

namespace Tryouts
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine(i);
                using (var a = new AttachmentFailover())
                {
                    await a.PutAttachmentsWithFailover_LowLevel();
                }
            }
        }
    }
}
