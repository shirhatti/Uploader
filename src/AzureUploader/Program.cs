using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Dnx.Runtime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using Microsoft.Dnx.Runtime.Common.CommandLine;
using Microsoft.Framework.Configuration;
using Newtonsoft.Json;

namespace AzureUploader
{
    public class Program
    {
        private class ConnectionException : Exception {}
        private IConfiguration Configuration { get; set; }
        private readonly string _accountName;
        private readonly string _accountKey;
        private readonly string _directory;

        public Program(IApplicationEnvironment appEnv)
        {
            var builder = new ConfigurationBuilder();
            builder.SetBasePath(appEnv.ApplicationBasePath);
            builder.AddJsonFile("config.json", optional:true);
            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
            _accountName = Configuration["AccountName"] ?? string.Empty;
            _accountKey = Configuration["AccountKey"] ?? string.Empty;
            _directory = Configuration["Directory"] ?? string.Empty;

        }

        private CloudStorageAccount Connect()
        {
            CloudStorageAccount storageAccount;
            try
            {
                storageAccount = new CloudStorageAccount(new StorageCredentials(_accountName, _accountKey), true);
            }
            catch (FormatException)
            {
                throw new ConnectionException();
            }

            catch (ArgumentException)
            {
                throw new ConnectionException();
            }
            return storageAccount;
        }
        public int Main(string[] args)
        {
            try
            {
                var app = new CommandLineApplication {};
                app.Name = "Azure Uploader";
                app.Description = "App can be used to upload files to Azure Blob Storage";

                app.HelpOption("-?|-h|--help");

                app.Command("test", c =>
                {
                    c.Description = "Test Azure connection credentials";

                    c.OnExecute(() =>
                    {
                        var storageAccount = Connect();
                        Console.WriteLine("Successfully validated your connection string");
                        return 0;
                    });
                });

                app.Command("json", c =>
                {
                    c.Description = "Produce an index.json file for uploading to blob storage";

                    c.OnExecute(async () =>
                    {
                        var storageAccount = Connect();
                        var blobClient = storageAccount.CreateCloudBlobClient();
                        var container = blobClient.GetContainerReference("$root");
                        var blockBlob = container.GetBlockBlobReference("index.json");

                        List<Channel> channels;

                        try
                        {
                            var index = await blockBlob.DownloadTextAsync();
                            channels = JsonConvert.DeserializeObject<List<Channel>>(index);
                            var stable = channels.First(channel => channel.Name == "stable");
                            stable.LastModifieDateTime = DateTime.Now;

                            var unstable = channels.First(channel => channel.Name == "unstable");
                            unstable.LastModifieDateTime = DateTime.Now;

                            var dev = channels.First(channel => channel.Name == "dev");
                            dev.LastModifieDateTime = DateTime.Now;

                            Console.WriteLine(JsonConvert.SerializeObject(channels, Formatting.Indented));
                        }
                        catch (Exception e)
                        {
                            var innerException = e.InnerException as StorageException;
                            if (innerException == null)
                            {
                                throw;
                            }
                            if (innerException.RequestInformation.HttpStatusCode == 404)
                            {
                                Console.WriteLine(
                                    "No index.json file was found at the specified location. Check the storage account and/or container name");
                                return 1;
                            }
                            throw;
                        }

                        try
                        {
                            await blockBlob.UploadTextAsync(JsonConvert.SerializeObject(channels, Formatting.Indented));
                        }
                        catch (StorageException)
                        {
                            return 1;
                        }
                        return 0;
                    });
                });

                app.Command("list", c =>
                {
                    c.Description = "List latest dnx drops";

                    c.OnExecute(() =>
                    {
                        if (!Directory.Exists(_directory))
                        {
                            Console.WriteLine("Specified directory does not exist");
                            return 1;
                        }
                        var files = Directory.EnumerateFiles(_directory)
                            .Where(
                                file =>
                                    (Path.GetFileName(file ?? string.Empty).StartsWith("dnx-clr") ||
                                     Path.GetFileName(file ?? string.Empty).StartsWith("dnx-coreclr")));
                        foreach (var file in files)
                        {
                            Console.WriteLine(Path.GetFileName(file));
                        }
                        return 0;
                    });
                });

                app.Command("upload", c =>
                {
                    c.Description = "Upload latest drops to a blob container";

                    var containerName = c.Option("--container", "Container name", CommandOptionType.SingleValue);

                    c.OnExecute(async () =>
                    {
                        var storageAccount = Connect();
                        var blobClient = storageAccount.CreateCloudBlobClient();
                        var container = blobClient.GetContainerReference(containerName.Value() ?? "$root");
                        try
                        {
                            await container.CreateIfNotExistsAsync();
                            await
                                container.SetPermissionsAsync(new BlobContainerPermissions
                                {
                                    PublicAccess = BlobContainerPublicAccessType.Blob
                                });
                        }
                        catch (StorageException)
                        {
                            return 1;
                        }
                        if (!Directory.Exists(_directory))
                        {
                            return 0;
                        }
                        var files = Directory.EnumerateFiles(_directory)
                            .Where(
                                file =>
                                    (Path.GetFileName(file ?? string.Empty).StartsWith("dnx-clr") ||
                                     Path.GetFileName(file ?? string.Empty).StartsWith("dnx-coreclr")));
                        foreach (var filePath in files)
                        {
                            var file = Path.GetFileName(filePath);
                            var blockBlob = container.GetBlockBlobReference(file);
                            await blockBlob.UploadFromFileAsync(filePath, FileMode.Open);
                            Console.WriteLine(file);
                        }
                        return 0;
                    });
                });

                app.OnExecute(() =>
                {
                    app.ShowHelp();
                    return 2;
                });
                return app.Execute(args);
            }
            catch (ConnectionException)
            {
                Console.WriteLine("Invalid storage account information provided. Please confirm the AccountName and AccountKey are valid.");
                return 1;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
        }
    }
}
