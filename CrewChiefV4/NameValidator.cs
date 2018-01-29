﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CrewChiefV4
{
    class NameValidator
    {

        // TODO: add more undeserving shitbags to this list as and when they crawl out the woodwork
        // Mostly for wrecking but some notable exceptions - sangalli for thinking it's ok to threaten people, 
        // hance, hotdog and koch for being extraordinarily ignorant and rude, and so on. My app, my rules :)
         private static HashSet<String> wankers = new HashSet<String>(StringComparer.InvariantCultureIgnoreCase) { "mr.sisterfister", "bigsilverhotdog", 
             "paul hance", "aline senna", "giuseppe sangalli", "patrick förster", "chris iwaski", "gazman", "peter koch",
             "andreas christiansen", "greg metcalf" /* twat...*/, "Aditas H1Z1Cases.com." };
 
          public static void validateName(String name)
          {
             if (wankers.Contains(name))
             {
                 throw new NameValidationException(name);
             }
          }
    }

    /**
     * special exception for special players so they can grumble that the app is spamming an exception. I'd call it something
     * more colourful but my usual charm deserts me.
     */
    class NameValidationException : Exception
    {
        public NameValidationException(string message)
            : base(message) { }
    }
}
