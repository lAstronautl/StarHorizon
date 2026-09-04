{
  KiB
    : Number -> Number
    = fun n => n * 1024,
  MiB
    : Number -> Number
    = fun n => (KiB n) * 1024
}
