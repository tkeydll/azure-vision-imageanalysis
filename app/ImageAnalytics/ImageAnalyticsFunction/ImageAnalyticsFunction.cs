using Azure;
using Azure.AI.Vision.ImageAnalysis;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Storage.Files.Shares;
using System.Linq;
using Azure.Storage.Blobs;


namespace ImageAnalyticsFunction
{
    public class ImageAnalyticsFunction
    {
        [Singleton(Mode = SingletonMode.Function)]
        [FunctionName("AnalyzeImage")]
        public static async Task Run(
            [BlobTrigger("images/source/{name}", Connection = "AzureWebJobsStorage")] Stream myBlob,
            string name,
            [Blob("images/correct/{name}", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobClient correctBlob,
            [Blob("images/invalid/{name}", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobClient invalidBlob,
            ILogger log)
        {
            log.LogInformation($"C# Blob trigger function Processed blob Name:{name} Size: {myBlob.Length} Bytes");

            await Task.Delay(5000);

            bool result = false;

            //bool result = await AnalyzeImageAsync(myBlob);
            Console.WriteLine($"result: {result}");

            if (result)
            {
                await correctBlob.UploadAsync(myBlob);
            }
            else
            {
                await invalidBlob.UploadAsync(myBlob);
            }
        }

        static async Task<bool> AnalyzeImageAsync(Stream imageStream)
        {
            string endpoint = Environment.GetEnvironmentVariable("VISION_ENDPOINT");
            string key = Environment.GetEnvironmentVariable("VISION_KEY");

            ImageAnalysisClient client = new ImageAnalysisClient(
                new Uri(endpoint),
                new AzureKeyCredential(key));

            BinaryData imageData = await BinaryData.FromStreamAsync(imageStream);

            ImageAnalysisResult result = await client.AnalyzeAsync(
                imageData,
                VisualFeatures.Tags | VisualFeatures.Read,
                new ImageAnalysisOptions { GenderNeutralCaption = false });

            Console.WriteLine($"result: {JsonConvert.SerializeObject(result.Tags)}");

            // ”»’è
            bool judge = ContainsHumanTags(result);

            return judge;
        }


        private static readonly string[] targetTags = Environment.GetEnvironmentVariable("TARGET_TAGS").Split(',');

        static bool ContainsHumanTags(ImageAnalysisResult result)
        {
            var tags = result.Tags.Values.Where(x => x.Confidence > 0.5);
            foreach (var tag in tags)
            {
                if (Array.Exists(targetTags, element => element == tag.Name))
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// Azure Files ‚É•Û‘¶
        /// </summary>
        /// <param name="imageStream"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        static async Task SaveToAzureFilesAsync(Stream imageStream, string folderName, string fileName)
        {
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            string shareName = Environment.GetEnvironmentVariable("SHARE_NAME");

            ShareClient share = new ShareClient(connectionString, shareName);
            ShareDirectoryClient directory = share.GetDirectoryClient(folderName);
            ShareFileClient file = directory.GetFileClient(fileName);

            await file.CreateAsync(imageStream.Length);
            await file.UploadAsync(imageStream);
        }



    }
}
