using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace Marmoset
{
    public class MarmosetInfo : GH_AssemblyInfo
    {
        public override string Name => "Marmoset";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("f9e74d8d-3137-4eeb-8f68-361cb9701d46");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}