using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class FOWViewer : MonoBehaviour
{
    [Range(0, 50)]
    [SerializeField] private int                            viewer_range_ = 7;
    private readonly List<FOWCulling>                       culling_comp_list_ = new List<FOWCulling>();
    [SerializeField] private SphereCollider                 collider_;




    public int ViewerRange
    {
        get
        {
            return viewer_range_;
        }
        set
        {
            viewer_range_ = value;
            ResetRange();
        }
    }
    public List<FOWCulling> CullingCompList => culling_comp_list_;
    




    private void Reset()
    {
        Init();
    }
    private void OnValidate()
    {
        ResetRange();
    }
    private void Start()
    {
        Init();
    }
    private void Init()
    {
        if (collider_ == null)
        {
            collider_ = GetComponent<SphereCollider>();
        }
    }
    private void ResetRange()
    {
        if(collider_ != null)
        {
            collider_.radius = viewer_range_;
        }
    }





    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent<FOWCulling>(out var comp))
        {
            return;
        }

        culling_comp_list_.Add(comp);
    }
    private void OnTriggerExit(Collider other)
    {
        if (!other.TryGetComponent<FOWCulling>(out var comp))
        {
            return;
        }

        comp.SetRenderEnabled(FOWManager.instance_ == null);
        culling_comp_list_.Remove(comp);
    }
    public void ResetViewer()
    {
        culling_comp_list_.Clear();
    }
    public void RemoveCullingComp(FOWCulling comp)
    {
        if(comp == null)
        {
            return;
        }

        culling_comp_list_.Remove(comp);
    }
    public void AddCullingComp(FOWCulling comp)
    {
        if (comp == null)
        {
            return;
        }

        culling_comp_list_.Add(comp);
    } 
}

