using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using RTSEngine.Audio;

[System.Serializable]
public class GameAudioManagerData
{
    public AudioData audioData;

    public GameAudioManagerData(GameAudioManager gameAudioManager)
    {
        this.audioData = gameAudioManager.Data;
    }
}
