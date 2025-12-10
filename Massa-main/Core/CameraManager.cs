using System;
using System.Collections.Generic;

namespace MassaKWin.Core
{
    public class CameraManager
    {
        public IList<Camera> Cameras { get; } = new List<Camera>();

        public void AddCamera(Camera camera)
        {
            Cameras.Add(camera);
        }

        public void RemoveCamera(Camera camera)
        {
            Cameras.Remove(camera);
        }
    }
}
