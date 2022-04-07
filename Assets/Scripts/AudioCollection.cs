using UnityEngine;


[CreateAssetMenu]
public class AudioCollection : ScriptableObject
{
	public WeightedObject<AudioClip>[] m_collection;


	public AudioClip Random() => m_collection.RandomWeighted();
}
