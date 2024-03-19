namespace HackMdBackup;

using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using System;
using System.Threading.Tasks;

public class S3Uploader
{
    private readonly string _bucketName;
    private static readonly RegionEndpoint BucketRegion = RegionEndpoint.USWest2; // change this based on your MinIO region

    private static IAmazonS3? _s3Client;

    public S3Uploader(string s3Host, string s3AccessKey, string s3SecretKey, string s3Bucket)
    {
        _bucketName = s3Bucket;
        _s3Client = new AmazonS3Client(s3AccessKey, s3SecretKey, new AmazonS3Config
        {
            ServiceURL = s3Host, 
            ForcePathStyle = true,
            SignatureVersion = "s3v4",
        });

    }
    
    public async Task UploadFileAsync(string filePath)
    {
        try
        {
            var fileTransferUtility = new TransferUtility(_s3Client);

            // Option 1. Upload a file. The file name is used as the object key name.
            await fileTransferUtility.UploadAsync(filePath, _bucketName);
        }
        catch (Exception e)
        {
            throw new Exception("Unable to upload file to S3", e);
        }
    }
}
