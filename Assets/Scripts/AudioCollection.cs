using UnityEngine;


[CreateAssetMenu(menuName = "ScriptableObject/Audio Collection", fileName = "AudioCollection", order = 0)]
public class AudioCollection : ScriptableObject
{
	public WeightedObject<AudioClip>[] m_collection;


	public AudioClip Random() => m_collection.RandomWeighted();
}
