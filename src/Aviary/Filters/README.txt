Aviary's ad blocker reads the filter lists in this folder.

  easylist.txt               EasyList                     ads              https://easylist.to/easylist/easylist.txt
  easyprivacy.txt            EasyPrivacy                  trackers         https://easylist.to/easylist/easyprivacy.txt
  fanboy-cookiemonster.txt   EasyList Cookie List         cookie notices   https://secure.fanboy.co.nz/fanboy-cookiemonster.txt
  fanboy-social.txt          Fanboy's Social Blocking     social widgets   https://easylist.to/easylist/fanboy-social.txt

All four are written and maintained by the EasyList authors (https://easylist.to/) and
are used here unmodified. Their licences, as each file's own header states:

  EasyList, EasyPrivacy, Fanboy's Social   Creative Commons Attribution-ShareAlike 3.0
                                           (https://creativecommons.org/licenses/by-sa/3.0/),
                                           also available under the GNU General Public
                                           License version 3
                                           (https://www.gnu.org/licenses/gpl-3.0.html).
                                           Licence page: https://easylist.to/pages/licence.html

  EasyList Cookie List                     Creative Commons Attribution 3.0
                                           (http://creativecommons.org/licenses/by/3.0/),
                                           as named in the file's own header.

Ads and Trackers are on when Aviary is installed. Cookie notices and Social widgets are
off until switched on in Settings -> Shields; a list that is off is not read at all.

Aviary does not change these files and does not mix them into any list of its own. The
copies here are the ones Aviary shipped with: EasyList and EasyPrivacy downloaded on
23 October 2026 (version 202610231316), the Cookie List and Fanboy's Social on 19 September 2026. "Update
now" in Settings, and the Daily or Weekly refresh, save newer copies in the profile
folder (filters\) instead; Aviary uses whichever copy the list itself says is newer.
"Use the shipped lists" in Settings deletes those downloaded copies and comes back to
the files in this folder.

Rules written by hand in Settings -> Shields -> My filters are not kept here. They live
in the profile's settings and are compiled in as a list called "My filters".

Aviary's own short list of tracker hosts is in TrackerBlocker.cs and is separate from
these.
