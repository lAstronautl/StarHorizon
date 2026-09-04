{
  EvenNumber = fun label value =>
    if value % 2 == 0 then
      value
    else
      std.contract.blame_with_message "value is not even" label
}
