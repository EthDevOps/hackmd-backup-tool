namespace HackMdBackup;

using System.IO;
using SharpCompress.Common;
using SharpCompress.Writers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

public class FileProcessor
{
    public void CreateTarGz(string outputPath, string inputDirectory)
    {
        using Stream targetStream = File.OpenWrite(outputPath);
        using var writer = WriterFactory.Open(targetStream, ArchiveType.Tar, CompressionType.GZip);
        writer.WriteAll(inputDirectory, searchPattern: "*", SearchOption.AllDirectories);
    }

   public void EncryptFile(string inputFile, string publicKeyFile, string outputFile, bool armor, bool withIntegrityCheck)
   {
       using Stream publicKeyStream = File.OpenRead(publicKeyFile);
       PgpPublicKey encKey = ReadPublicKey(publicKeyStream);

       using MemoryStream bOut = new MemoryStream();
       PgpCompressedDataGenerator comData = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);

       PgpUtilities.WriteFileToLiteralData(
           comData.Open(bOut),
           PgpLiteralData.Binary,
           new FileInfo(inputFile));

       comData.Close();

       PgpEncryptedDataGenerator cPk = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Cast5, withIntegrityCheck, new SecureRandom());

       cPk.AddMethod(encKey);

       byte[] bytes = bOut.ToArray();

       using Stream outputStream = File.Create(outputFile);
       if (armor)
       {
           using Stream armoredStream = new ArmoredOutputStream(outputStream);
           WriteBytesToStream(cPk.Open(armoredStream, bytes.Length), bytes);
       }
       else
       {
           WriteBytesToStream(cPk.Open(outputStream, bytes.Length), bytes);
       }
   }

    private static void WriteBytesToStream(Stream outputStream, byte[] bytes)
    {
        using Stream encryptedOut = outputStream;
        encryptedOut.Write(bytes, 0, bytes.Length);
    }
    
    private static PgpPublicKey ReadPublicKey(Stream inputStream)
    {
        using Stream keyIn = inputStream;
        PgpPublicKeyRingBundle pgpPub = new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));

        foreach (PgpPublicKeyRing keyRing in pgpPub.GetKeyRings())
        {
            foreach (PgpPublicKey key in keyRing.GetPublicKeys())
            {
                if (key.IsEncryptionKey)
                {
                    return key;
                }
            }
        }

        throw new ArgumentException("Can't find encryption key in key ring.");
    }

}