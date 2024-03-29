using System.Collections.Generic;

using UnityEngine;

using RTSEngine;
using RTSEngine.EntityComponent;
using RTSEngine.UI;
using RTSEngine.ResourceExtension;

public class EntitySeller : EntityComponentBase
{
    #region Attributes
    [SerializeField]
    private EntityComponentTaskUIAsset taskUI = null;

    [SerializeField]
    private ResourceInput[] sellResources = new ResourceInput[0];

    protected IResourceManager resourceMgr { private set; get; }
    #endregion

    #region Init
    protected override void OnInit()
    {
        if(!Entity.IsFactionEntity())
        {
            Debug.LogError($"[EntitySeller] This component can only be attached to unit or building entities!", gameObject);
            return;
        }

        resourceMgr = gameMgr.GetService<IResourceManager>();
    }
    #endregion

    #region Task UI
    protected override bool OnTaskUICacheUpdate(List<EntityComponentTaskUIAttributes> taskUIAttributesCache, List<string> disabledTaskCodesCache)
    {
        return RTSHelper.OnSingleTaskUIRequest(
            this,
            taskUIAttributesCache,
            disabledTaskCodesCache,
            taskUI);
    }

    public override bool OnTaskUIClick(EntityComponentTaskUIAttributes taskAttributes)
    {
        if (taskUI.IsValid() && taskAttributes.data.code == taskUI.Key)
        {
            resourceMgr.UpdateResource(Entity.FactionID, sellResources, add: true);
            Entity.Health.Destroy(upgrade: false, null);
            return true;
        }

        return false;
    }
    #endregion
}
