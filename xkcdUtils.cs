using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using Size = System.Drawing.Size;

namespace XKCDHoverboardGameImageDownloader
{
   static class xkcdUtils
   {
      #region Downloading

      public static void DownloadImages()
      {

         var rangeToCheckX = new Range<int> {Minimum = 950, Maximum = 1500};
         var rangeToCheckY = new Range<int> {Minimum = 999, Maximum = 1400};
         const int numberOfThreads = 8;

         var numberOfXToCheck = rangeToCheckX.Maximum - rangeToCheckX.Minimum;
         var eachThreadXCount = numberOfXToCheck/numberOfThreads;
         for ( var threadNumber = 0; threadNumber < numberOfThreads; threadNumber++ )
         {
            var threadXStartingPoint = rangeToCheckX.Minimum + eachThreadXCount*threadNumber;
            var threadXEndingPoint = threadXStartingPoint + eachThreadXCount;

            var imageGrabberThread = new Thread(() =>
            {
               for (var x = threadXStartingPoint; x < threadXEndingPoint; x++)
               {
                  for (var y = rangeToCheckY.Minimum; y <= rangeToCheckY.Maximum; y++)
                  {
                     using (WebClient client = new WebClient())
                     {
                        //exmaple image name: http://xkcd.com/1608/992:-1111+s.png
                        var remoteImagePath = "http://xkcd.com/1608/" + x + ":-" + y + "+s.png";
                        var localImagePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) +
                                             @"\Hoverboard\" + +x + "x" + y + ".png";
                        ;
                        try
                        {
                           client.DownloadFile(remoteImagePath, localImagePath);
                           Console.WriteLine(remoteImagePath + " success");
                        }
                        catch (Exception e)
                        {
                           Console.WriteLine(remoteImagePath + " failure");
                        }
                     }
                  }
               }
            });

            imageGrabberThread.Start();
         }
      }

      #endregion Downloading

      #region Image Manipulation

      public static void MassageImages()
      {
         var dialog = new System.Windows.Forms.FolderBrowserDialog();
         dialog.ShowDialog();

         var tileImageAssets = TileImageAssetsFromDirectory(dialog.SelectedPath);

         var xMin = tileImageAssets.OrderBy(x => x.X).FirstOrDefault().X;
         var yMin = tileImageAssets.OrderBy(x => x.Y).FirstOrDefault().Y;
         var yMax = tileImageAssets.OrderByDescending(x => x.Y).FirstOrDefault().Y;

         //normalize image values we need to do this on the first pass, shouldn't be necessary after we've massaged the data
         foreach ( var tileImageAsset in tileImageAssets )
         {
            tileImageAsset.X -= xMin;
            tileImageAsset.Y -= yMin;
         }
         yMax -= yMin;

         //flip y values
         foreach ( var tileImageAsset in tileImageAssets )
         {
            tileImageAsset.Y = yMax - tileImageAsset.Y;
         }

         var rootDirectoryForConversion = dialog.SelectedPath + "\\converted";
         Directory.CreateDirectory( rootDirectoryForConversion );

         const int desiredTileSize = 256;

         var zoomLevel = 0;
         var scaleFactor = (int)Math.Pow(2,zoomLevel);
         var lastCombinedSizeOfTiles = ConvertTilesToNewTileSize(tileImageAssets, rootDirectoryForConversion + "\\"+zoomLevel, desiredTileSize, scaleFactor);

         var needsOneMorePass = true;
         while ( ( lastCombinedSizeOfTiles.Width > desiredTileSize || lastCombinedSizeOfTiles.Height > desiredTileSize ) || needsOneMorePass )
         {
            if ( ( lastCombinedSizeOfTiles.Width > desiredTileSize || lastCombinedSizeOfTiles.Height > desiredTileSize ) && needsOneMorePass )
            {
               needsOneMorePass = false;
            }
            zoomLevel++;
            var destinationDirectory = rootDirectoryForConversion + "\\" + zoomLevel;
            scaleFactor = (int)Math.Pow( 2, zoomLevel );
            lastCombinedSizeOfTiles = ConvertTilesToNewTileSize( tileImageAssets, destinationDirectory, desiredTileSize, scaleFactor );
         }

         //reverse order the directories
         var directoriesToSwitch = Directory.GetDirectories(rootDirectoryForConversion);
         var lastDirectoryIndex = directoriesToSwitch.Length - 1;
         //give them their temp names
         foreach ( var directoryPath in directoriesToSwitch )
         {
            var splitPath = directoryPath.Split('\\');
            var currentName = splitPath[splitPath.Length - 1];
            var directoryIntValue = Convert.ToInt32(currentName);
            var newDirectoryName = rootDirectoryForConversion + "\\temp" + (lastDirectoryIndex - directoryIntValue);
            Directory.Move( directoryPath, newDirectoryName );
         }
         //remove the temp
         directoriesToSwitch = Directory.GetDirectories( rootDirectoryForConversion );
         foreach ( var directoryPath in directoriesToSwitch )
         {
            var splitPath = directoryPath.Split('\\');
            var currentName = splitPath[splitPath.Length - 1];
            var newDirectoryName = rootDirectoryForConversion +"\\"+ currentName.Replace("temp","");
            Directory.Move( directoryPath, newDirectoryName );
         }

         MessageBox.Show( "Successfully converted!", "Success", MessageBoxButton.OK, MessageBoxImage.Information );
      }

      private class ImageAsset
      {
         public string BitmapPath { get; set; }
         public int X { get; set; }
         public int Y { get; set; }
      }

      private static List<ImageAsset> TileImageAssetsFromDirectory( string directoryPath )
      {
         var tiledImagePaths = new List<string>(Directory.GetFiles(directoryPath));
         var tileImageAssets = new List<ImageAsset>();

         foreach ( var tiledImagePath in tiledImagePaths )
         {
            var brokenPath = tiledImagePath.Split('\\');
            var fileName = brokenPath[brokenPath.Length - 1];
            fileName = fileName.Split( '.' )[0];
            var brokenFileName = fileName.Split('x');
            tileImageAssets.Add( new ImageAsset { BitmapPath = tiledImagePath, X = Convert.ToInt32( brokenFileName[0] ), Y = Convert.ToInt32( brokenFileName[1] ) } );
         }
         return tileImageAssets;
      }

      private static Size ConvertTilesToNewTileSize( List<ImageAsset> tileImageAssets, string destinationDirectory, int desiredTileSize, int scaleFactor )
      {
         Directory.CreateDirectory( destinationDirectory );

         var xMax = tileImageAssets.OrderByDescending(x => x.X).FirstOrDefault().X;
         var yMax = tileImageAssets.OrderByDescending(x => x.Y).FirstOrDefault().Y;

         var firstImage = new Bitmap(tileImageAssets[0].BitmapPath);
         var sourceTileWidth = firstImage.Width;
         var sourceTileHeight = firstImage.Height;
         var sourceTiledImageWidth = ((xMax + 1) * sourceTileWidth) / scaleFactor;
         var sourceTiledImageHeight = ((yMax + 1) * sourceTileHeight) / scaleFactor;

         var lengthOfTilesToConvertX = Convert.ToInt32( Math.Ceiling( sourceTiledImageWidth / (double)desiredTileSize ) );
         var lengthOfTilesToConvertY = Convert.ToInt32( Math.Ceiling( sourceTiledImageHeight / (double)desiredTileSize ) );

         //find empty space and get padding to center image in space
         var leftOverSpaceWidth = (lengthOfTilesToConvertX*desiredTileSize) - sourceTiledImageWidth;
         var leftOverSpaceHeight = (lengthOfTilesToConvertY*desiredTileSize) - sourceTiledImageHeight;
         var tilePaddingX = (leftOverSpaceWidth/2)*scaleFactor;
         var tilePaddingY = (leftOverSpaceHeight/2)*scaleFactor;

         //list of rects
         var rectMap = new List<Tuple<Rect, string>>();
         foreach ( var tileImageAsset in tileImageAssets )
         {
            var newTileX = tileImageAsset.X*sourceTileWidth;
            var newTileY = tileImageAsset.Y*sourceTileHeight;
            rectMap.Add( new Tuple<Rect, string>( new Rect( new System.Windows.Point( newTileX + tilePaddingX, newTileY + tilePaddingY ), new System.Windows.Size( sourceTileWidth, sourceTileHeight ) ), tileImageAsset.BitmapPath ) );
         }

         for ( var x = 0; x < lengthOfTilesToConvertX; x++ )
         {
            for ( var y = 0; y < lengthOfTilesToConvertY; y++ )
            {
               var tileX = x*(double) desiredTileSize;
               var tileY = y*(double) desiredTileSize;
               var tileWidth = (double) desiredTileSize;
               var tileHeight = (double) desiredTileSize;
               var tileRect = new Rect(new System.Windows.Point( tileX * scaleFactor, tileY * scaleFactor ), new System.Windows.Size( tileWidth * scaleFactor, tileHeight * scaleFactor ));
               var intersectingRectPairs = rectMap.FindAll( rectPair => rectPair.Item1.IntersectsWith( tileRect ) );

               using ( var bitmap = new Bitmap( (int)desiredTileSize, (int)desiredTileSize ) )
               {
                  using ( var canvas = Graphics.FromImage( bitmap ) )
                  {
                     canvas.InterpolationMode = InterpolationMode.Low;
                     //canvas.CompositingMode = CompositingMode.SourceOver;
                     //canvas.CompositingQuality = CompositingQuality.HighSpeed;
                     //canvas.SmoothingMode = SmoothingMode.HighSpeed;
                     canvas.Clear( Color.White );

                     foreach ( var intersectingRectPair in intersectingRectPairs )
                     {
                        var tileBitmap = new Bitmap(intersectingRectPair.Item2);

                        var imageX = (int) intersectingRectPair.Item1.X - (int) tileRect.X;
                        var imageY = (int) intersectingRectPair.Item1.Y - (int) tileRect.Y;
                        var imageWidth = (int) intersectingRectPair.Item1.Width;
                        var imageHeight = (int) intersectingRectPair.Item1.Height;
                        canvas.DrawImage( tileBitmap, new Rectangle( (int)( imageX / scaleFactor ), (int)( imageY / scaleFactor ), (int)( imageWidth / scaleFactor ), (int)( imageHeight / scaleFactor ) ), new Rectangle( 0, 0, (int)intersectingRectPair.Item1.Width, (int)intersectingRectPair.Item1.Height ), GraphicsUnit.Pixel );
                        tileBitmap.Dispose();
                     }
                     canvas.Save();
                  }
                  try
                  {
                     bitmap.Save( destinationDirectory + "\\" + x + "," + y + ".png", ImageFormat.Png );
                  }
                  catch ( Exception ex ) { }
               }
            }
         }

         return new Size( lengthOfTilesToConvertX * desiredTileSize, lengthOfTilesToConvertY * desiredTileSize );
      }

      #endregion Image Manipulation
   }

}
