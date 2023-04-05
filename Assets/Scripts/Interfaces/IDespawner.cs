using UnityEngine;


public interface IDespawner
{
	public Component Component => this as Component;


	public void DespawnAttachable(IAttachable attachable);
}
