using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

using UnityEngine;

using RTSEngine.Audio;

public static class SaveSystem
{
    public static void SaveAudioSetting(GameAudioManager gameAudioManager)
    {
        BinaryFormatter formatter = new BinaryFormatter();
        string path = Application.persistentDataPath + "/audio.fun";
        FileStream stream = new FileStream(path, FileMode.Create);

        GameAudioManagerData data = new GameAudioManagerData(gameAudioManager);

        formatter.Serialize(stream, data);
        stream.Close();
        Debug.Log("Save Completed: " + path);
    }

    public static GameAudioManagerData LoadAudioSetting()
    {
        string path = Application.persistentDataPath + "/audio.fun";

        if(File.Exists(path)) 
        {
            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream(path,  FileMode.Open);

            GameAudioManagerData data = formatter.Deserialize(stream) as GameAudioManagerData;
            Debug.Log("Load Completed: " + path);
            stream.Close();
            return data;
        }
        else
        {
            Debug.Log("Load Failed: " + path);
            return null;
        }
    }
}
