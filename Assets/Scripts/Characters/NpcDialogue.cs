using UnityEngine;


[CreateAssetMenu]
public class NpcDialogue : ScriptableObject
{
	public WeightedObject<string[]>[] m_dialogue;
}
