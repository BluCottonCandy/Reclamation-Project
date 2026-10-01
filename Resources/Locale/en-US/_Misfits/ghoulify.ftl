misfits-ghoul-feral-warning = Your muscles spasm briefly.
misfits-ghoul-feral-danger1 = Your muscles jerk violently.
misfits-ghoul-feral-danger2 = You snarl involuntarily.
misfits-ghoul-feral-danger3 = You have an overwhelming sense of dread.
misfits-ghoul-feral-critical = You move as through a dream. Everything is hazy. Everything will be alright soon.
misfits-ghoul-feral-complete = Your mind has succumbed to bestial instinct!

misfits-ghoul-feral-examine = { CAPITALIZE(SUBJECT($target)) } {CONJUGATE-BE($target)} twitching involuntarily.

misfits-ghoul-feral-danger1-others = jerks suddenly.
misfits-ghoul-feral-danger2-others = jerks and snarls!
misfits-ghoul-feral-critical-others = violently convulses and roars!

# I maybe did this one wrong but our guidebook is cooked so I can't really test it anyway
# Reagent guidebook
reagent-effect-guidebook-modify-feralization = Reduces the feralization counter on a ghoul that is turning feral ({ $chance ->
  [1] always
  *[other] { $chance } chance
  { $delta } Feral counter.
   Effective above { $threshold }.
}),
  
