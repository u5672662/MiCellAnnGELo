using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads a sequence of PNG slides from disk and displays them, with simple network-synchronised navigation.
/// </summary>
public class DisplayNotes : NetworkBehaviour
{
    private int notesWidth = 1000;
    private int notesHeight = 750;
    private Sprite[] slides;
    private int nSlides = 0;
    private int slideNumber = 0;
    private void Start()
    {
        
    }


    /// <summary>
    /// Loads all PNG files from a directory into a slide deck and displays the first slide.
    /// </summary>
    public void LoadDataNotes(string dirPath)
    {
        if (Directory.Exists(dirPath))
        {
            string[] slideFiles = Directory.GetFiles(dirPath, "*.png");
            nSlides = slideFiles.Length;
            Debug.Log("Total number of slides: " + nSlides);
            slides = new Sprite[nSlides];
            for (int i = 0; i < nSlides; i++)
            {
                Texture2D imageIn = new Texture2D(4, 4);
                byte[] imageData = File.ReadAllBytes(slideFiles[i]);
                imageIn.LoadImage(imageData);
                slides[i] = Sprite.Create((Texture2D)imageIn, new Rect(0, 0, notesWidth, notesHeight), Vector2.zero);
                Debug.Log("Slide " + i + " loaded.");
            }
            slideNumber = 0;
            if (nSlides > 0)
            {
                var img = GetComponent<Image>();
                if (img != null)
                    img.sprite = slides[slideNumber];
            }
        }
        else
        {
            Debug.Log("No notes found. Path: " + dirPath);
        }
    }

    public void PrevSlide()
    {
        if (nSlides > 0)
        {
            slideNumber--;
            slideNumber = (slideNumber + nSlides) % nSlides;
            var img = GetComponent<Image>();
            if (img != null)
                img.sprite = slides[slideNumber];
            SlideChangeServerRpc(slideNumber, NetworkManager.Singleton.LocalClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SlideChangeServerRpc(int slide, ulong clientId)
    {
        SlideChangeClientRpc(slide, clientId);
    }

    [ClientRpc]
    private void SlideChangeClientRpc(int slide, ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId)
        {
            slideNumber = slide;
            if (nSlides > 0)
            {
                var img = GetComponent<Image>();
                if (img != null)
                    img.sprite = slides[slideNumber];
            }
        }
    }
}