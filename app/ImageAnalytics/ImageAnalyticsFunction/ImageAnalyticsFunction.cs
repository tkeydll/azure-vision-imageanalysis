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
        private static int interval = Environment.GetEnvironmentVariable("INTERVAL") != null ? int.Parse(Environment.GetEnvironmentVariable("INTERVAL")) : 5000;
        private static string endpoint = Environment.GetEnvironmentVariable("VISION_ENDPOINT");
        private static string key = Environment.GetEnvironmentVariable("VISION_KEY");
        private static readonly string[] targetTags = Environment.GetEnvironmentVariable("TARGET_TAGS").Split(',');

        private static ILogger _log;


        [Singleton(Mode = SingletonMode.Function)]
        [FunctionName("AnalyzeImage")]
        public static async Task Run(
            [BlobTrigger("images/source/{name}", Connection = "AzureWebJobsStorage")] Stream myBlob,
            string name,
            [Blob("images/correct/{name}", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobClient correctBlob,
            [Blob("images/invalid/{name}", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobClient invalidBlob,
            [Blob("images/error/{name}", FileAccess.Write, Connection = "AzureWebJobsStorage")] BlobClient errorBlob,
            ILogger log)
        {
            log.LogInformation($"C# Blob trigger function Processed blob Name:{name} Size: {myBlob.Length} Bytes");

            _log = log;

            await Task.Delay(interval);

            try
            {
                bool result = await AnalyzeImageAsync(myBlob);
                //bool result = true;

                log.LogInformation($"result: {result}");

                myBlob.Position = 0;

                if (result)
                {
                    var uploadResult = await correctBlob.UploadAsync(myBlob, true);
                    log.LogInformation($"({uploadResult.GetRawResponse().Status}) Save to correct dir.");
                }
                else
                {
                    var uploadResult = await invalidBlob.UploadAsync(myBlob, true);
                    log.LogInformation($"({uploadResult.GetRawResponse().Status}) Save to invalid dir.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                var uploadResult = await errorBlob.UploadAsync(myBlob);
                log.LogInformation($"({uploadResult.GetRawResponse().Status}) Save to error dir.");
            }
        }


        static async Task<bool> AnalyzeImageAsync(Stream imageStream)
        {
            ImageAnalysisClient client = new ImageAnalysisClient(
                new Uri(endpoint),
                new AzureKeyCredential(key));

            BinaryData imageData = await BinaryData.FromStreamAsync(imageStream);

            ImageAnalysisResult result = await client.AnalyzeAsync(
                imageData,
                VisualFeatures.Tags | VisualFeatures.Read,
                new ImageAnalysisOptions { GenderNeutralCaption = false });

            _log.LogDebug($"result: {JsonConvert.SerializeObject(result.Tags)}");

            // ”»’è
            bool judge = ContainsHumanTags(result);

            return judge;
        }


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
