using System;
using UnityEngine;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Factory for creating importers, for each format.
    /// Use this if you only want to import a dataset, without deciding which importer to use.
    /// Some dataset formats can be imported using several different importers, in which case this factory will return the best alternative.
    /// </summary>
    public class ImporterFactory
    {
        /// <summary>
        /// Create an importer for an image sequence dataset (multiple files) of the specified format.
        /// Use this for image sequences.
        /// </summary>
        /// <param name="format">Format of the dataset.</param>
        /// <returns></returns>
        public static IImageSequenceImporter CreateImageSequenceImporter(ImageSequenceFormat format)
        {
            Type importerType = GetImageSequenceImporterType(format);
            if (importerType != null)
            {
                return (IImageSequenceImporter)Activator.CreateInstance(importerType);
            }
            else
            {
                Debug.LogError("No supported importer for format: " + format);
                return null;
            }
        }

        private static Type GetImageSequenceImporterType(ImageSequenceFormat format)
        {
            switch (format)
            {
                case ImageSequenceFormat.ImageSequence:
                    {
                        #if UVR_USE_SIMPLEITK
                        return typeof(SimpleITKImageSequenceImporter);
                        #else
                        return typeof(ImageSequenceImporter);
                        #endif
                    }
                default:
                    return null;
            }
        }
    }
}
