using UnityEngine;


public sealed class AudioClipRandomize : MonoBehaviour
{
	[SerializeField] private WeightedObject<AudioClip>[] m_clips;


	private void Awake()
	{
		AudioSource source = GetComponent<AudioSource>();
		source.clip = m_clips.RandomWeighted();
		source.Play(); // NOTE that we need this even if the source has PlayOnAwake enabled since changing the clip may have happened afterward
	}
}
